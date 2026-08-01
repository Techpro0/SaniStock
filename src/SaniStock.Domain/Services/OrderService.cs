using Microsoft.EntityFrameworkCore;
using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain.Models;

namespace SaniStock.Domain.Services;

/// <summary>
/// Books orders (reserving stock without touching on-hand) and cancels them
/// (releasing any undispatched reservation).
/// </summary>
public class OrderService
{
    private readonly SaniStockDbContext _db;
    private readonly StockService _stock;
    private readonly StockAllocationService _allocations;
    private readonly NumberSequenceService _numbers;
    private readonly IUserContext _user;

    public OrderService(SaniStockDbContext db, StockService stock, StockAllocationService allocations,
        NumberSequenceService numbers, IUserContext user)
    {
        _db = db;
        _stock = stock;
        _allocations = allocations;
        _numbers = numbers;
        _user = user;
    }

    /// <summary>
    /// Books an order. Increases Reserved for each line; never changes OnHand and never
    /// blocks on insufficient stock — a resulting negative Available is the shortfall signal.
    /// <para>
    /// Each item line is allocated against real stock by <see cref="StockAllocationService"/>, which
    /// lets a top-grade line be covered from the grade below when its own stock runs out. The
    /// reservation therefore lands on whichever grade physically holds the goods, and the per-source
    /// split is recorded on the line so dispatch and cancellation can unwind the same sources.
    /// </para>
    /// </summary>
    public Order Book(OrderInput input)
    {
        if (input.Lines.Count == 0 && input.AccessoryLines.Count == 0)
            throw new DomainException("An order must have at least one item or accessory line.");
        if (input.Lines.Any(l => l.Quantity <= 0) || input.AccessoryLines.Any(l => l.Quantity <= 0))
            throw new DomainException("All ordered quantities must be greater than zero.");
        if (!_db.Parties.Any(p => p.Id == input.PartyId))
            throw new DomainException("Selected party does not exist.");
        foreach (var brandId in input.Lines.Select(l => l.BrandId).Distinct())
        {
            if (!_db.Brands.Any(b => b.Id == brandId))
                throw new DomainException("Selected brand does not exist.");
        }

        var order = new Order
        {
            OrderNo = _numbers.NextOrderNo(input.OrderDate.Year),
            PartyId = input.PartyId,
            OrderDate = input.OrderDate,
            Status = OrderStatus.Booked,
            Remarks = input.Remarks,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now
        };

        // Keep item OrderLines index-aligned with input.Lines so we can resolve each line's
        // per-line accessory exclusions after ids are assigned.
        var orderLines = new List<OrderLine>();
        foreach (var l in input.Lines)
        {
            var ol = new OrderLine
            {
                ItemId = l.ItemId,
                GradeId = l.GradeId,
                ColourId = l.ColourId,
                BrandId = l.BrandId,
                QuantityOrdered = l.Quantity,
                QuantityDispatched = 0,
                QuantityReserved = l.Quantity
            };
            order.Lines.Add(ol);
            orderLines.Add(ol);
        }
        // Manually-added standalone accessory lines (not tied to any item line).
        foreach (var a in input.AccessoryLines)
        {
            order.AccessoryLines.Add(new OrderAccessoryLine
            {
                AccessoryId = a.AccessoryId,
                QuantityOrdered = a.Quantity,
                QuantityDispatched = 0,
                QuantityReserved = a.Quantity
            });
        }

        _db.Orders.Add(order);
        _db.SaveChanges(); // assign order + item-line ids

        // Auto-attach each item line's default accessories (recipe × ordered qty), minus any
        // the caller excluded for that line. These reserve stock exactly like standalone ones.
        for (int i = 0; i < input.Lines.Count; i++)
        {
            var lineInput = input.Lines[i];
            var parentLine = orderLines[i];
            var excluded = lineInput.ExcludedAccessoryIds is { Count: > 0 }
                ? new HashSet<int>(lineInput.ExcludedAccessoryIds)
                : null;

            var defaults = _db.ItemAccessoryDefaults
                .Where(d => d.ItemId == lineInput.ItemId && d.IsActive)
                .Select(d => new { d.AccessoryId, d.QtyPerUnit })
                .ToList();

            foreach (var d in defaults)
            {
                if (excluded != null && excluded.Contains(d.AccessoryId)) continue;
                var qty = d.QtyPerUnit * lineInput.Quantity;
                if (qty <= 0) continue;
                order.AccessoryLines.Add(new OrderAccessoryLine
                {
                    AccessoryId = d.AccessoryId,
                    SourceOrderLineId = parentLine.Id,
                    QuantityOrdered = qty,
                    QuantityDispatched = 0,
                    QuantityReserved = qty
                });
            }
        }

        // Allocate line by line, applying each line's reservation before planning the next, so two
        // lines competing for the same stock (or a 1st-grade line borrowing the very 2nd-grade stock
        // a later line asks for) see each other's claims rather than both counting it as free.
        foreach (var l in order.Lines)
        {
            var steps = _allocations.Plan(l.ItemId, l.GradeId, l.ColourId, l.BrandId, l.QuantityOrdered);
            foreach (var s in steps)
                l.Allocations.Add(new OrderLineAllocation
                {
                    GradeId = s.GradeId,
                    BrandId = s.BrandId,
                    Bucket = s.Bucket,
                    Priority = s.Priority,
                    Quantity = s.Quantity
                });

            // One reservation movement per balance row touched — that is, per grade+brand, brand
            // null being the shared unpacked pool. Reserved is not bucket-split within a row, so
            // two rows for the same source would be identical in the ledger; the bucket detail
            // belongs to the allocation rows, which is where it stays meaningful.
            foreach (var bySource in steps.GroupBy(s => new { s.GradeId, s.BrandId }))
            {
                _stock.ApplyFinished(l.ItemId, bySource.Key.GradeId, l.ColourId, bySource.Key.BrandId,
                    StockMovementType.Reservation, deltaRawOnHand: 0, deltaPackedOnHand: 0,
                    deltaReserved: bySource.Sum(s => s.Quantity),
                    order.OrderDate, "Order", order.Id,
                    DescribeAllocation(order.OrderNo, l.GradeId, bySource.Key.GradeId, bySource));
            }
        }
        foreach (var a in order.AccessoryLines)
        {
            _stock.ApplyAccessory(a.AccessoryId,
                StockMovementType.Reservation, deltaOnHand: 0, deltaReserved: a.QuantityOrdered,
                order.OrderDate, "Order", order.Id, $"Booking {order.OrderNo}");
        }

        _stock.AddAudit("OrderBooked", "Order", order.Id, $"Booked {order.OrderNo} for party {order.PartyId}");
        _db.SaveChanges();
        return order;
    }

    /// <summary>
    /// Cancels an order, releasing the still-reserved (undispatched) quantity on every line.
    /// Already-dispatched quantity is unaffected. Fully-dispatched orders cannot be cancelled.
    /// </summary>
    public void Cancel(int orderId, string? reason = null)
    {
        var order = _db.Orders
            .Include(o => o.Lines)
            .Include(o => o.AccessoryLines)
            .FirstOrDefault(o => o.Id == orderId)
            ?? throw new DomainException("Order not found.");

        if (order.Status == OrderStatus.Cancelled)
            throw new DomainException("Order is already cancelled.");
        if (order.Status == OrderStatus.Dispatched)
            throw new DomainException("A fully dispatched order cannot be cancelled.");

        // Release against the exact rows the line drew from — grade and brand both — unwinding in
        // reverse draw order so a borrowed lower grade is given back before the line's own grade.
        foreach (var l in order.Lines.Where(l => l.QuantityReserved > 0))
        {
            var draws = _allocations.PlanRelease(l, l.QuantityReserved);
            StockAllocationService.MarkReleased(draws);
            foreach (var (gradeId, brandId, qty) in StockAllocationService.BySource(draws))
            {
                _stock.ApplyFinished(l.ItemId, gradeId, l.ColourId, brandId,
                    StockMovementType.ReservationRelease, deltaRawOnHand: 0, deltaPackedOnHand: 0,
                    deltaReserved: -qty, DateTime.Today, "Order", order.Id, $"Cancel {order.OrderNo}");
            }
            l.QuantityReserved = 0;
        }
        foreach (var a in order.AccessoryLines.Where(a => a.QuantityReserved > 0))
        {
            _stock.ApplyAccessory(a.AccessoryId,
                StockMovementType.ReservationRelease, deltaOnHand: 0, deltaReserved: -a.QuantityReserved,
                DateTime.Today, "Order", order.Id, $"Cancel {order.OrderNo}");
            a.QuantityReserved = 0;
        }

        order.Status = OrderStatus.Cancelled;
        _stock.AddAudit("OrderCancelled", "Order", order.Id,
            $"Cancelled {order.OrderNo}. {reason}".Trim());
        _db.SaveChanges();
    }

    /// <summary>
    /// Ledger remark spelling out what a reservation was covered from, e.g.
    /// "Booking ORD-2026-0007 (packed 50 for Brand A)", noting when one grade covered another's line.
    /// All the steps in one group share a source row, so the brand is stated once for the group.
    /// </summary>
    private string DescribeAllocation(string orderNo, int lineGradeId, int sourceGradeId,
        IEnumerable<AllocationStep> steps)
    {
        var stepList = steps.ToList();
        var detail = string.Join(", ", stepList.Select(s => s.Bucket switch
        {
            StockBucket.Packed => $"packed {s.Quantity:0.###}",
            StockBucket.Raw => $"unpacked {s.Quantity:0.###}",
            _ => $"not in stock {s.Quantity:0.###}"
        }));

        // Only branded (packed) draws name a brand; the unpacked pool and shortfalls have none.
        var brandId = stepList[0].BrandId;
        var text = brandId is int b
            ? $"Booking {orderNo} ({detail} for {BrandName(b)})"
            : $"Booking {orderNo} ({detail})";

        return sourceGradeId == lineGradeId
            ? text
            : $"{text} — covering a {GradeName(lineGradeId)} grade line";
    }

    private string GradeName(int gradeId) =>
        _db.Grades.Where(g => g.Id == gradeId).Select(g => g.Name).FirstOrDefault() ?? "?";

    private string BrandName(int brandId) =>
        _db.Brands.Where(b => b.Id == brandId).Select(b => b.Name).FirstOrDefault() ?? "?";
}
