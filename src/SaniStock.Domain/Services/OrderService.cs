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
    private readonly NumberSequenceService _numbers;
    private readonly IUserContext _user;

    public OrderService(SaniStockDbContext db, StockService stock, NumberSequenceService numbers, IUserContext user)
    {
        _db = db;
        _stock = stock;
        _numbers = numbers;
        _user = user;
    }

    /// <summary>
    /// Books an order. Increases Reserved for each line; never changes OnHand and never
    /// blocks on insufficient stock — a resulting negative Available is the shortfall signal.
    /// </summary>
    public Order Book(OrderInput input)
    {
        if (input.Lines.Count == 0 && input.AccessoryLines.Count == 0)
            throw new DomainException("An order must have at least one item or accessory line.");
        if (input.Lines.Any(l => l.Quantity <= 0) || input.AccessoryLines.Any(l => l.Quantity <= 0))
            throw new DomainException("All ordered quantities must be greater than zero.");
        if (!_db.Parties.Any(p => p.Id == input.PartyId))
            throw new DomainException("Selected party does not exist.");

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

        foreach (var l in order.Lines)
        {
            _stock.ApplyFinished(l.ItemId, l.GradeId, l.ColourId,
                StockMovementType.Reservation, deltaOnHand: 0, deltaReserved: l.QuantityOrdered,
                order.OrderDate, "Order", order.Id, $"Booking {order.OrderNo}");
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

        foreach (var l in order.Lines.Where(l => l.QuantityReserved > 0))
        {
            _stock.ApplyFinished(l.ItemId, l.GradeId, l.ColourId,
                StockMovementType.ReservationRelease, deltaOnHand: 0, deltaReserved: -l.QuantityReserved,
                DateTime.Today, "Order", order.Id, $"Cancel {order.OrderNo}");
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
}
