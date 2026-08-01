using Microsoft.EntityFrameworkCore;
using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain.Models;

namespace SaniStock.Domain.Services;

/// <summary>
/// Ships booked quantities against an order: reduces both OnHand and Reserved by the
/// dispatched amount, advances the order line and order status, and issues a dispatch note.
/// <para>
/// Goods come out of the grades the line's reservation was actually drawn from (a 1st-grade line
/// may have been covered from 2nd-grade stock), and within each grade out of the line's brand's
/// packed stock first, falling back to the shared unpacked pool when packed runs short.
/// </para>
/// <para>
/// Brand adds no new rule here — the allocation rows already say where each reservation sits — but
/// it does split the bookkeeping in two. A reservation is released from the row it was recorded
/// against, while the goods may physically leave the sibling row (stock booked while unpacked and
/// packed under this brand before shipping, or vice versa). So one physical draw can write two
/// movements: the Reserved leg on the recorded row, the on-hand leg on the row the pieces are
/// actually in. Other brands' packed stock is never touched.
/// </para>
/// </summary>
public class DispatchService
{
    private readonly SaniStockDbContext _db;
    private readonly StockService _stock;
    private readonly StockAllocationService _allocations;
    private readonly NumberSequenceService _numbers;
    private readonly IUserContext _user;

    public DispatchService(SaniStockDbContext db, StockService stock, StockAllocationService allocations,
        NumberSequenceService numbers, IUserContext user)
    {
        _db = db;
        _stock = stock;
        _allocations = allocations;
        _numbers = numbers;
        _user = user;
    }

    public DispatchEntry Dispatch(DispatchInput input)
    {
        var order = _db.Orders
            .Include(o => o.Lines)
            .Include(o => o.AccessoryLines)
            .FirstOrDefault(o => o.Id == input.OrderId)
            ?? throw new DomainException("Order not found.");

        if (order.Status == OrderStatus.Cancelled)
            throw new DomainException("Cannot dispatch a cancelled order.");
        if (order.Status == OrderStatus.Dispatched)
            throw new DomainException("Order is already fully dispatched.");

        // Fold repeated entries for the same line together so one line is validated and drawn once.
        var lineInputs = input.Lines
            .Where(l => l.Quantity != 0)
            .GroupBy(l => l.OrderLineId)
            .Select(g => new DispatchLineInput(g.Key, g.Sum(x => x.Quantity)))
            .ToList();
        var accInputs = input.AccessoryLines
            .Where(l => l.Quantity != 0)
            .GroupBy(l => l.OrderAccessoryLineId)
            .Select(g => new DispatchAccessoryLineInput(g.Key, g.Sum(x => x.Quantity)))
            .ToList();
        if (lineInputs.Count == 0 && accInputs.Count == 0)
            throw new DomainException("Nothing to dispatch — enter at least one quantity.");

        // Validate everything before mutating any stock.
        foreach (var li in lineInputs)
        {
            var line = order.Lines.FirstOrDefault(l => l.Id == li.OrderLineId)
                ?? throw new DomainException("Dispatch references a line that is not on this order.");
            if (li.Quantity < 0)
                throw new DomainException("Dispatch quantity cannot be negative.");
            if (li.Quantity > line.QuantityPending)
                throw new DomainException(
                    $"Cannot dispatch {li.Quantity}; only {line.QuantityPending} pending on that line.");
        }
        foreach (var ai in accInputs)
        {
            var line = order.AccessoryLines.FirstOrDefault(l => l.Id == ai.OrderAccessoryLineId)
                ?? throw new DomainException("Dispatch references an accessory line that is not on this order.");
            if (ai.Quantity < 0)
                throw new DomainException("Dispatch quantity cannot be negative.");
            if (ai.Quantity > line.QuantityPending)
                throw new DomainException(
                    $"Cannot dispatch {ai.Quantity}; only {line.QuantityPending} pending on that accessory line.");
        }

        // Work out which grade each line draws from before touching anything. Planning is read-only,
        // so a rejected dispatch leaves the allocations exactly as they were.
        var plans = lineInputs.ToDictionary(
            li => li.OrderLineId,
            li => _allocations.PlanDispatch(order.Lines.First(l => l.Id == li.OrderLineId), li.Quantity));

        // Aggregate per stock key: one dispatch may draw the same item+grade+colour+brand via
        // several lines, and the physical split has to be decided against the combined quantity.
        // Each key tracks its two demands separately — what was reserved against the shared
        // unpacked pool and what was reserved against this brand's packed stock — because those
        // are different rows whose Reserved must move independently, even when the goods all come
        // out of one of them.
        var needed = new Dictionary<(int ItemId, int GradeId, int ColourId, int BrandId), StockNeed>();
        foreach (var (orderLineId, draws) in plans)
        {
            var line = order.Lines.First(l => l.Id == orderLineId);
            foreach (var d in draws)
            {
                var key = (line.ItemId, d.GradeId, line.ColourId, line.BrandId);
                needed.TryGetValue(key, out var running);
                needed[key] = d.BrandId is null
                    ? running with { AgainstPool = running.AgainstPool + d.Quantity }
                    : running with { AgainstBrand = running.AgainstBrand + d.Quantity };
            }
        }

        // You cannot ship goods that have not been produced yet. Booking may run stock negative
        // (that is the shortfall signal), but dispatch can never take out more than is physically
        // in stock — packed under this brand, or still unpacked and free to be packed under it.
        // Another brand's packed pieces are in the wrong boxes and deliberately do not count.
        //
        // Each brand's packed row belongs to exactly one key, but the unpacked pool is SHARED: two
        // keys differing only in brand draw on the same physical pieces. So the pool is rationed as
        // the keys are planned — without that running total, two brands on one order would each
        // validate against the whole pool and together ship stock that does not exist. Whichever
        // key is planned first gets the pool, matching how contention is settled everywhere else.
        var splits = new Dictionary<(int ItemId, int GradeId, int ColourId, int BrandId), PhysicalDraw>();
        var poolLeft = new Dictionary<(int ItemId, int GradeId, int ColourId), decimal>();
        foreach (var (key, need) in needed)
        {
            var poolKey = (key.ItemId, key.GradeId, key.ColourId);
            if (!poolLeft.TryGetValue(poolKey, out var rawOnHand))
                rawOnHand = poolLeft[poolKey] =
                    _stock.FindFinishedBalance(key.ItemId, key.GradeId, key.ColourId, null)?.RawOnHand ?? 0m;

            var packedOnHand = _stock
                .FindFinishedBalance(key.ItemId, key.GradeId, key.ColourId, key.BrandId)?.PackedOnHand ?? 0m;

            // Where the goods come from is decided by what is physically there, packed first;
            // where the reservation is released from is decided by `need`, which remembers the row
            // each part was booked against. The two can differ, which is why they are tracked apart.
            var split = StockAllocationService.PlanPhysicalDraw(need.Total, rawOnHand, packedOnHand);
            splits[key] = split;
            poolLeft[poolKey] = rawOnHand - split.FromRaw;

            if (split.Total < need.Total)
                throw new DomainException(
                    $"Not enough stock to send {DescribeFinished(key.ItemId, key.GradeId, key.ColourId)} " +
                    $"({BrandName(key.BrandId)}). In stock for this brand: {rawOnHand + packedOnHand:0.###} " +
                    $"(packed {packedOnHand:0.###}, unpacked {rawOnHand:0.###}), " +
                    $"trying to send: {need.Total:0.###}. Produce more first.");
        }

        foreach (var grp in accInputs
                     .Select(ai => new { Line = order.AccessoryLines.First(l => l.Id == ai.OrderAccessoryLineId), ai.Quantity })
                     .GroupBy(x => x.Line.AccessoryId))
        {
            var accNeeded = grp.Sum(x => x.Quantity);
            var onHand = _db.AccessoryStockBalances
                .Where(b => b.AccessoryId == grp.Key).Select(b => b.OnHand).FirstOrDefault();
            if (accNeeded > onHand)
            {
                var name = _db.Accessories.Where(a => a.Id == grp.Key).Select(a => a.Name).FirstOrDefault() ?? "accessory";
                throw new DomainException(
                    $"Not enough stock to send {name}. In stock: {onHand:0.###}, trying to send: {accNeeded:0.###}. Add stock first.");
            }
        }

        var dispatch = new DispatchEntry
        {
            DispatchNo = _numbers.NextDispatchNo(input.Date.Year),
            OrderId = order.Id,
            Date = input.Date,
            DispatchedBy = _user.Username,
            Remarks = input.Remarks,
            CreatedAt = DateTime.Now
        };
        foreach (var li in lineInputs)
            dispatch.Lines.Add(new DispatchLine { OrderLineId = li.OrderLineId, Quantity = li.Quantity });
        foreach (var ai in accInputs)
            dispatch.AccessoryLines.Add(new DispatchAccessoryLine { OrderAccessoryLineId = ai.OrderAccessoryLineId, Quantity = ai.Quantity });

        _db.DispatchEntries.Add(dispatch);
        _db.SaveChanges(); // assign ids

        // Take the goods out. Each key touches up to two rows: the shared unpacked pool and this
        // brand's packed stock. Reserved comes off the row it was booked against; on-hand comes off
        // the row the pieces are actually in. Usually those are the same row and the second leg is
        // a no-op, so it is skipped rather than written as an all-zero movement.
        foreach (var (key, need) in needed)
        {
            var split = splits[key];
            var remark = $"Dispatch {dispatch.DispatchNo} (packed {split.FromPacked:0.###}, " +
                         $"unpacked {split.FromRaw:0.###}, {BrandName(key.BrandId)})";

            if (split.FromRaw != 0 || need.AgainstPool != 0)
                _stock.ApplyFinished(key.ItemId, key.GradeId, key.ColourId, brandId: null,
                    StockMovementType.Dispatch, deltaRawOnHand: -split.FromRaw, deltaPackedOnHand: 0,
                    deltaReserved: -need.AgainstPool, input.Date, "DispatchEntry", dispatch.Id, remark);

            if (split.FromPacked != 0 || need.AgainstBrand != 0)
                _stock.ApplyFinished(key.ItemId, key.GradeId, key.ColourId, key.BrandId,
                    StockMovementType.Dispatch, deltaRawOnHand: 0, deltaPackedOnHand: -split.FromPacked,
                    deltaReserved: -need.AgainstBrand, input.Date, "DispatchEntry", dispatch.Id, remark);
        }

        foreach (var li in lineInputs)
        {
            var line = order.Lines.First(l => l.Id == li.OrderLineId);
            StockAllocationService.MarkDispatched(plans[li.OrderLineId]);
            line.QuantityDispatched += li.Quantity;
            line.QuantityReserved -= li.Quantity;
        }
        foreach (var ai in accInputs)
        {
            var line = order.AccessoryLines.First(l => l.Id == ai.OrderAccessoryLineId);
            _stock.ApplyAccessory(line.AccessoryId,
                StockMovementType.Dispatch, deltaOnHand: -ai.Quantity, deltaReserved: -ai.Quantity,
                input.Date, "DispatchEntry", dispatch.Id, $"Dispatch {dispatch.DispatchNo}");
            line.QuantityDispatched += ai.Quantity;
            line.QuantityReserved -= ai.Quantity;
        }

        order.Status = ComputeStatus(order);
        _stock.AddAudit("Dispatch", "DispatchEntry", dispatch.Id,
            $"{dispatch.DispatchNo} against {order.OrderNo}");
        _db.SaveChanges();

        return dispatch;
    }

    /// <summary>
    /// One dispatch key's demand, kept split by the row it was reserved against: the shared
    /// unpacked pool versus the line's brand's packed stock. The goods may come out of either row,
    /// but the reservation must be released from the row that is actually holding it.
    /// </summary>
    private readonly record struct StockNeed(decimal AgainstPool, decimal AgainstBrand)
    {
        public decimal Total => AgainstPool + AgainstBrand;
    }

    /// <summary>A readable "Item / Grade / Colour" label for error messages.</summary>
    private string DescribeFinished(int itemId, int gradeId, int colourId)
    {
        var item = _db.Items.Where(x => x.Id == itemId).Select(x => x.Name).FirstOrDefault() ?? "item";
        var grade = _db.Grades.Where(x => x.Id == gradeId).Select(x => x.Name).FirstOrDefault() ?? "?";
        var colour = _db.Colours.Where(x => x.Id == colourId).Select(x => x.Name).FirstOrDefault() ?? "?";
        return $"{item} / {grade} / {colour}";
    }

    private string BrandName(int brandId) =>
        _db.Brands.Where(x => x.Id == brandId).Select(x => x.Name).FirstOrDefault() ?? "?";

    /// <summary>Booked (nothing shipped), PartiallyDispatched (some), or Dispatched (all lines complete).</summary>
    private static OrderStatus ComputeStatus(Order order)
    {
        bool anyDispatched = order.Lines.Any(l => l.QuantityDispatched > 0)
                             || order.AccessoryLines.Any(l => l.QuantityDispatched > 0);
        bool allComplete = order.Lines.All(l => l.QuantityPending == 0)
                           && order.AccessoryLines.All(l => l.QuantityPending == 0);

        if (allComplete) return OrderStatus.Dispatched;
        return anyDispatched ? OrderStatus.PartiallyDispatched : OrderStatus.Booked;
    }
}
