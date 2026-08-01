using Microsoft.EntityFrameworkCore;
using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;
using Xunit;

namespace SaniStock.Domain.Tests;

/// <summary>
/// Covers editing a booked order. An edit is Cancel-then-Book on one order: the reservation is
/// released against the exact rows it came from, the superseded lines are zeroed but kept, and the
/// new lines run through the same allocation path <c>Book</c> uses.
/// <para>
/// Editing is only allowed while nothing has shipped, so these tests lean on that guard as much as
/// on the re-allocation itself.
/// </para>
/// </summary>
public class OrderEditTests
{
    // ---- Re-allocation --------------------------------------------------------

    [Fact]
    public void Editing_a_quantity_re_reserves_the_new_amount_and_keeps_the_order_number()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 30)));
        var orderNo = order.OrderNo;
        Assert.Equal(30, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);

        h.Orders.Edit(order.Id, Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 70)));

        Assert.Equal(70, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
        Assert.Equal(30, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Available);
        Assert.Equal(orderNo, h.Db.Orders.AsNoTracking().Single(o => o.Id == order.Id).OrderNo);
    }

    [Fact]
    public void Editing_down_hands_the_difference_back()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 80)));

        h.Orders.Edit(order.Id, Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 20)));

        Assert.Equal(20, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
        Assert.Equal(80, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Available);
    }

    /// <summary>
    /// Changing the brand has to release the old brand's packed stock and claim the new one's —
    /// the reason an edit re-plans from scratch rather than adjusting quantities in place.
    /// </summary>
    [Fact]
    public void Editing_the_brand_moves_the_reservation_to_the_other_brands_stock()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.PackSplit(h.ItemA, h.Grade1, h.White, (h.BrandA, 50), (h.BrandB, 50));
        var order = h.Orders.Book(Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 40)));
        Assert.Equal(40, h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA).Reserved);

        h.Orders.Edit(order.Id, Order(h, (h.ItemA, h.Grade1, h.White, h.BrandB, 40)));

        Assert.Equal(0, h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA).Reserved);
        Assert.Equal(40, h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandB).Reserved);
    }

    [Fact]
    public void Editing_re_runs_the_grade_chain_exactly_as_booking_would()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 40, null));
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade2, h.White, 40, null));
        var order = h.Orders.Book(Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 10)));

        // 60 needs all of 1st grade and 20 of 2nd — the same walk a fresh booking would take.
        h.Orders.Edit(order.Id, Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 60)));

        var line = h.Db.OrderLines.AsNoTracking()
            .Single(l => l.OrderId == order.Id && l.QuantityOrdered > 0);
        Assert.Equal(new[]
        {
            (h.Grade1, (int?)null, StockBucket.Raw, 40m),
            (h.Grade2, (int?)null, StockBucket.Raw, 20m),
        }, h.BrandAllocation(line.Id));
        Assert.Equal(40, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
        Assert.Equal(20, h.FinishedBalance(h.ItemA, h.Grade2, h.White).Reserved);
    }

    [Fact]
    public void Editing_replaces_the_bundled_accessories_too()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 2m) });
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 200, null));
        var order = h.Orders.Book(Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 10)));
        Assert.Equal(20, h.AccessoryBalance(h.Acc1).Reserved);   // 2 per unit × 10

        h.Orders.Edit(order.Id, Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 30)));

        Assert.Equal(60, h.AccessoryBalance(h.Acc1).Reserved);   // 2 per unit × 30, not 20 + 60
    }

    // ---- Nothing is deleted ---------------------------------------------------

    /// <summary>
    /// The superseded line stays on the order at zero quantity, with its allocation rows, so the
    /// history of what was originally booked survives the edit.
    /// </summary>
    [Fact]
    public void The_superseded_lines_are_kept_at_zero_rather_than_removed()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 30)));
        var originalLineId = order.Lines.Single().Id;

        h.Orders.Edit(order.Id, Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 45)));

        var lines = h.Db.OrderLines.AsNoTracking().Where(l => l.OrderId == order.Id).ToList();
        Assert.Equal(2, lines.Count);

        var superseded = lines.Single(l => l.Id == originalLineId);
        Assert.Equal(0, superseded.QuantityOrdered);
        Assert.Equal(0, superseded.QuantityReserved);
        Assert.NotEmpty(h.Db.OrderLineAllocations.AsNoTracking().Where(a => a.OrderLineId == originalLineId));

        // Only the live line counts.
        Assert.Equal(45, lines.Single(l => l.QuantityOrdered > 0).QuantityOrdered);
    }

    [Fact]
    public void Superseded_lines_are_hidden_from_the_orders_report()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 30)));

        h.Orders.Edit(order.Id, Order(h, (h.ItemB, h.Grade1, h.White, h.BrandA, 15)));

        var rows = h.Reports.GetOrders(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1))
            .Where(r => r.OrderNo == order.OrderNo).ToList();
        var row = Assert.Single(rows);
        Assert.Equal(15, row.Ordered);
    }

    // ---- Guards ---------------------------------------------------------------

    [Fact]
    public void An_order_with_anything_dispatched_cannot_be_edited()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 30)));
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 5) },
            Array.Empty<DispatchAccessoryLineInput>()));

        Assert.Throws<DomainException>(() =>
            h.Orders.Edit(order.Id, Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 50))));

        // The refused edit changed nothing: 25 still reserved, 5 already gone.
        Assert.Equal(25, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
        Assert.Equal(95, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
    }

    [Fact]
    public void An_order_with_only_an_accessory_dispatched_cannot_be_edited_either()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m) });
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        var order = h.Orders.Book(Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 30)));

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(),
            new[] { new DispatchAccessoryLineInput(order.AccessoryLines.Single().Id, 5) }));

        Assert.Throws<DomainException>(() =>
            h.Orders.Edit(order.Id, Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 50))));
    }

    [Fact]
    public void A_deleted_order_cannot_be_edited()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 30)));
        h.Orders.Cancel(order.Id);

        Assert.Throws<DomainException>(() =>
            h.Orders.Edit(order.Id, Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 10))));

        Assert.Equal(0, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
    }

    [Fact]
    public void Editing_to_an_empty_order_is_rejected()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 30)));

        Assert.Throws<DomainException>(() => h.Orders.Edit(order.Id, new OrderInput(
            h.PartyX, DateTime.Today, null,
            Array.Empty<OrderLineInput>(), Array.Empty<OrderAccessoryLineInput>())));

        Assert.Equal(30, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
    }

    /// <summary>
    /// An edit that cannot be fully covered still books — booking is never blocked by a lack of
    /// stock, and the uncovered part becomes the shortfall signal, exactly as on a fresh order.
    /// </summary>
    [Fact]
    public void Editing_beyond_available_stock_records_a_shortfall_rather_than_failing()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 50, null));
        var order = h.Orders.Book(Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 20)));

        h.Orders.Edit(order.Id, Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 80)));

        Assert.Equal(80, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
        Assert.Equal(-30, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Available);

        var line = h.Db.OrderLines.AsNoTracking().Single(l => l.OrderId == order.Id && l.QuantityOrdered > 0);
        Assert.Contains(h.BrandAllocation(line.Id), a => a.Bucket == StockBucket.Shortfall && a.Quantity == 30m);
    }

    [Fact]
    public void An_edited_order_can_still_be_dispatched_and_deleted()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 30)));
        h.Orders.Edit(order.Id, Order(h, (h.ItemA, h.Grade1, h.White, h.BrandA, 50)));

        var liveLine = h.Db.OrderLines.AsNoTracking().Single(l => l.OrderId == order.Id && l.QuantityOrdered > 0);
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(liveLine.Id, 20) }, Array.Empty<DispatchAccessoryLineInput>()));

        Assert.Equal(80, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
        Assert.Equal(30, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);

        h.Orders.Cancel(order.Id);
        Assert.Equal(0, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
        Assert.Equal(OrderStatus.Cancelled, h.Db.Orders.AsNoTracking().Single(o => o.Id == order.Id).Status);
    }

    // ---- Helpers --------------------------------------------------------------

    private static OrderInput Order(TestHarness h,
        params (int ItemId, int GradeId, int ColourId, int BrandId, decimal Qty)[] lines) =>
        new(h.PartyX, DateTime.Today, null,
            lines.Select(l => new OrderLineInput(l.ItemId, l.GradeId, l.ColourId, l.BrandId, l.Qty)).ToList(),
            Array.Empty<OrderAccessoryLineInput>());
}
