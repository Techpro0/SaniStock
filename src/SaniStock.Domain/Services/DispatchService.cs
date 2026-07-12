using Microsoft.EntityFrameworkCore;
using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain.Models;

namespace SaniStock.Domain.Services;

/// <summary>
/// Ships booked quantities against an order: reduces both OnHand and Reserved by the
/// dispatched amount, advances the order line and order status, and issues a dispatch note.
/// </summary>
public class DispatchService
{
    private readonly SaniStockDbContext _db;
    private readonly StockService _stock;
    private readonly NumberSequenceService _numbers;
    private readonly IUserContext _user;

    public DispatchService(SaniStockDbContext db, StockService stock, NumberSequenceService numbers, IUserContext user)
    {
        _db = db;
        _stock = stock;
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

        var lineInputs = input.Lines.Where(l => l.Quantity != 0).ToList();
        var accInputs = input.AccessoryLines.Where(l => l.Quantity != 0).ToList();
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

        // You cannot ship goods that have not been produced yet. Booking may run stock negative
        // (that is the shortfall signal), but dispatch can never take out more than is physically
        // in stock. Aggregate per stock key in case one dispatch draws the same item twice.
        foreach (var grp in lineInputs
                     .Select(li => new { Line = order.Lines.First(l => l.Id == li.OrderLineId), li.Quantity })
                     .GroupBy(x => new { x.Line.ItemId, x.Line.GradeId, x.Line.ColourId }))
        {
            var needed = grp.Sum(x => x.Quantity);
            var onHand = _db.StockBalances
                .Where(b => b.ItemId == grp.Key.ItemId && b.GradeId == grp.Key.GradeId && b.ColourId == grp.Key.ColourId)
                .Select(b => b.OnHand).FirstOrDefault();
            if (needed > onHand)
                throw new DomainException(
                    $"Not enough stock to send {DescribeFinished(grp.Key.ItemId, grp.Key.GradeId, grp.Key.ColourId)}. " +
                    $"In stock: {onHand:0.###}, trying to send: {needed:0.###}. Produce more first.");
        }

        foreach (var grp in accInputs
                     .Select(ai => new { Line = order.AccessoryLines.First(l => l.Id == ai.OrderAccessoryLineId), ai.Quantity })
                     .GroupBy(x => x.Line.AccessoryId))
        {
            var needed = grp.Sum(x => x.Quantity);
            var onHand = _db.AccessoryStockBalances
                .Where(b => b.AccessoryId == grp.Key).Select(b => b.OnHand).FirstOrDefault();
            if (needed > onHand)
            {
                var name = _db.Accessories.Where(a => a.Id == grp.Key).Select(a => a.Name).FirstOrDefault() ?? "accessory";
                throw new DomainException(
                    $"Not enough stock to send {name}. In stock: {onHand:0.###}, trying to send: {needed:0.###}. Add stock first.");
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

        foreach (var li in lineInputs)
        {
            var line = order.Lines.First(l => l.Id == li.OrderLineId);
            _stock.ApplyFinished(line.ItemId, line.GradeId, line.ColourId,
                StockMovementType.Dispatch, deltaOnHand: -li.Quantity, deltaReserved: -li.Quantity,
                input.Date, "DispatchEntry", dispatch.Id, $"Dispatch {dispatch.DispatchNo}");
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

    /// <summary>A readable "Item / Grade / Colour" label for error messages.</summary>
    private string DescribeFinished(int itemId, int gradeId, int colourId)
    {
        var item = _db.Items.Where(x => x.Id == itemId).Select(x => x.Name).FirstOrDefault() ?? "item";
        var grade = _db.Grades.Where(x => x.Id == gradeId).Select(x => x.Name).FirstOrDefault() ?? "?";
        var colour = _db.Colours.Where(x => x.Id == colourId).Select(x => x.Name).FirstOrDefault() ?? "?";
        return $"{item} / {grade} / {colour}";
    }

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
