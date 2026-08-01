using Microsoft.EntityFrameworkCore;
using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;
using Xunit;

namespace SaniStock.Domain.Tests;

/// <summary>
/// Covers reversing a dispatch — putting shipped goods back into the exact buckets and brand rows
/// they left from, re-raising the reservation, and un-consuming the allocations behind it.
/// <para>
/// The bucket-and-brand maths is where this could go quietly wrong, so most of these assert the
/// individual balance rows rather than the combination's total: a reversal that put packed stock
/// back as unpacked, or into the wrong brand, would leave the total correct and the rows wrong.
/// </para>
/// </summary>
public class DispatchReversalTests
{
    // ---- Stock goes back where it came from -----------------------------------

    [Fact]
    public void Reversing_a_packed_dispatch_puts_the_stock_back_in_that_brands_packed_row()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 60, h.BrandA);
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 40));
        var dispatch = Ship(h, order, 40);

        // A branded row never holds unpacked stock, so Raw stays 0 — the 40 that is still unpacked
        // sits on the shared pool row, not here.
        Assert.Equal((0m, 20m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA));
        Assert.Equal((40m, 0m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, null));

        h.Dispatch.Reverse(dispatch.Id);

        // Back to 60 packed for Brand A, and holding the 40 again.
        Assert.Equal((0m, 60m, 40m), h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA));
        Assert.Equal((40m, 0m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, null));
        Assert.Equal((100m, 40m, 60m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
    }

    /// <summary>
    /// The case a per-line "from packed / from raw" pair would get wrong: one shipped line drawing
    /// from a brand's packed row and the shared pool at once. Each has to go back to its own row.
    /// </summary>
    [Fact]
    public void Reversing_a_dispatch_that_spanned_both_rows_restores_each_row_separately()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 30, h.BrandA);
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 90));
        var dispatch = Ship(h, order, 90);

        // 30 packed + 60 from the pool went out.
        Assert.Equal((0m, 0m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA));
        Assert.Equal((10m, 0m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, null));

        h.Dispatch.Reverse(dispatch.Id);

        Assert.Equal((0m, 30m, 30m), h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA));
        Assert.Equal((70m, 0m, 60m), h.BrandRow(h.ItemA, h.Grade1, h.White, null));
        Assert.Equal((100m, 90m, 10m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
    }

    /// <summary>
    /// The other case a per-line pair cannot express: one shipped line drawing across two grades.
    /// </summary>
    [Fact]
    public void Reversing_a_dispatch_that_borrowed_a_second_grade_restores_both_grades()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 40, null));
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade2, h.White, 40, null));
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 60));
        var dispatch = Ship(h, order, 60);

        Assert.Equal(0, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
        Assert.Equal(20, h.FinishedBalance(h.ItemA, h.Grade2, h.White).OnHand);

        h.Dispatch.Reverse(dispatch.Id);

        Assert.Equal((40m, 40m, 0m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
        Assert.Equal((40m, 20m, 20m), h.FinishedBalance(h.ItemA, h.Grade2, h.White));
    }

    [Fact]
    public void Reversing_never_puts_stock_into_another_brands_row()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.PackSplit(h.ItemA, h.Grade1, h.White, (h.BrandA, 40), (h.BrandB, 40));
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 40));
        var dispatch = Ship(h, order, 40);

        h.Dispatch.Reverse(dispatch.Id);

        Assert.Equal(40, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandA));
        Assert.Equal(40, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandB));
        Assert.Equal(0, h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandB).Reserved);
    }

    // ---- Order and allocation state -------------------------------------------

    [Fact]
    public void Reversing_restores_the_order_line_and_its_status()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 50));
        var dispatch = Ship(h, order, 50);
        Assert.Equal(OrderStatus.Dispatched, Status(h, order.Id));

        h.Dispatch.Reverse(dispatch.Id);

        var line = h.Db.OrderLines.AsNoTracking().Single(l => l.OrderId == order.Id);
        Assert.Equal(0, line.QuantityDispatched);
        Assert.Equal(50, line.QuantityReserved);
        Assert.Equal(OrderStatus.Booked, Status(h, order.Id));
    }

    [Fact]
    public void Reversing_a_partial_dispatch_returns_the_order_to_partially_dispatched()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 60));
        Ship(h, order, 20);
        var second = Ship(h, order, 30);
        Assert.Equal(OrderStatus.PartiallyDispatched, Status(h, order.Id));

        h.Dispatch.Reverse(second.Id);

        var line = h.Db.OrderLines.AsNoTracking().Single(l => l.OrderId == order.Id);
        Assert.Equal(20, line.QuantityDispatched);
        Assert.Equal(40, line.QuantityReserved);
        Assert.Equal(OrderStatus.PartiallyDispatched, Status(h, order.Id));
    }

    /// <summary>
    /// The un-consume walk has to hand quantity back to the same allocations the dispatch took it
    /// from — reverse priority order, mirroring the ascending walk that consumed them.
    /// </summary>
    [Fact]
    public void Reversing_un_consumes_the_allocations_it_consumed()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 30, h.BrandA);
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 90));
        var lineId = order.Lines.Single().Id;

        Ship(h, order, 20);   // eats into the packed allocation only
        var before = Consumed(h, lineId);
        var second = Ship(h, order, 50);
        Assert.NotEqual(before, Consumed(h, lineId));

        h.Dispatch.Reverse(second.Id);

        // Exactly the state after the first dispatch, allocation by allocation.
        Assert.Equal(before, Consumed(h, lineId));
    }

    [Fact]
    public void Reversing_restores_bundled_accessories_too()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 2m) });
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 200, null));
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 30));
        var accLineId = order.AccessoryLines.Single().Id;

        var dispatch = h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 30) },
            new[] { new DispatchAccessoryLineInput(accLineId, 60) }));
        Assert.Equal((140m, 0m, 140m), h.AccessoryBalance(h.Acc1));

        h.Dispatch.Reverse(dispatch.Id);

        // 200 on hand again, and holding the 60 for the order once more.
        Assert.Equal((200m, 60m, 140m), h.AccessoryBalance(h.Acc1));
        Assert.Equal(0, h.Db.OrderAccessoryLines.AsNoTracking().Single(a => a.Id == accLineId).QuantityDispatched);
    }

    // ---- The reversing record --------------------------------------------------

    [Fact]
    public void Reversing_writes_a_linked_note_and_leaves_the_original_alone()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 40));
        var dispatch = Ship(h, order, 40);

        var reversal = h.Dispatch.Reverse(dispatch.Id);

        Assert.True(reversal.IsReversal);
        Assert.Equal(dispatch.Id, reversal.ReversesEntryId);
        Assert.NotEqual(dispatch.DispatchNo, reversal.DispatchNo);

        var stillThere = h.Db.DispatchEntries.AsNoTracking().Single(d => d.Id == dispatch.Id);
        Assert.False(stillThere.IsReversal);
        Assert.Equal(2, h.Db.DispatchEntries.AsNoTracking().Count(d => d.OrderId == order.Id));
    }

    // ---- Guards ----------------------------------------------------------------

    [Fact]
    public void A_dispatch_cannot_be_reversed_twice_and_a_reversal_cannot_be_reversed()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 40));
        var dispatch = Ship(h, order, 40);
        var reversal = h.Dispatch.Reverse(dispatch.Id);

        Assert.Throws<DomainException>(() => h.Dispatch.Reverse(dispatch.Id));
        Assert.Throws<DomainException>(() => h.Dispatch.Reverse(reversal.Id));

        // Neither refusal double-counted anything.
        Assert.Equal((100m, 40m, 60m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
    }

    /// <summary>
    /// The ordering constraint. A later dispatch consumed allocations on top of this one, so
    /// unwinding out of turn would give quantity back to the wrong sources.
    /// </summary>
    [Fact]
    public void An_earlier_dispatch_cannot_be_reversed_while_a_later_one_stands()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 60));
        var first = Ship(h, order, 20);
        Ship(h, order, 30);

        var ex = Assert.Throws<DomainException>(() => h.Dispatch.Reverse(first.Id));
        Assert.Contains("Reverse the later one first", ex.Message);

        // Nothing moved.
        Assert.Equal(50, h.Db.OrderLines.AsNoTracking().Single(l => l.OrderId == order.Id).QuantityDispatched);
    }

    [Fact]
    public void Reversing_the_later_dispatch_first_then_the_earlier_one_unwinds_everything()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 40, h.BrandA);
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 60));
        var first = Ship(h, order, 20);
        var second = Ship(h, order, 30);

        h.Dispatch.Reverse(second.Id);
        h.Dispatch.Reverse(first.Id);   // now the latest live one

        // Exactly as booked: nothing sent, 60 held, stock untouched and split as it was packed.
        var line = h.Db.OrderLines.AsNoTracking().Single(l => l.OrderId == order.Id);
        Assert.Equal(0, line.QuantityDispatched);
        Assert.Equal(60, line.QuantityReserved);
        Assert.Equal((60m, 40m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
        Assert.Equal(40, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandA));
        Assert.Equal((100m, 60m, 40m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
    }

    // ---- It composes with everything else --------------------------------------

    /// <summary>Reversing the only dispatch makes an order editable again.</summary>
    [Fact]
    public void Reversing_the_only_dispatch_makes_the_order_editable_again()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 40));
        var dispatch = Ship(h, order, 10);

        Assert.Throws<DomainException>(() => h.Orders.Edit(order.Id, Line(h, h.Grade1, h.BrandA, 55)));

        h.Dispatch.Reverse(dispatch.Id);
        h.Orders.Edit(order.Id, Line(h, h.Grade1, h.BrandA, 55));

        Assert.Equal(55, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
        Assert.Equal(100, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
    }

    [Fact]
    public void A_fully_dispatched_order_can_be_reversed_and_then_deleted()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 40));
        var dispatch = Ship(h, order, 40);

        Assert.Throws<DomainException>(() => h.Orders.Cancel(order.Id));   // fully sent

        h.Dispatch.Reverse(dispatch.Id);
        h.Orders.Cancel(order.Id);

        Assert.Equal(OrderStatus.Cancelled, Status(h, order.Id));
        Assert.Equal((100m, 0m, 100m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
    }

    /// <summary>
    /// The ledger is the source of truth, so a reversal has to survive a rebuild — if the inverted
    /// legs were wrong, reconcile would disagree with the cached balances.
    /// </summary>
    [Fact]
    public void Reconcile_reproduces_the_balances_after_a_reversal()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.PackSplit(h.ItemA, h.Grade1, h.White, (h.BrandA, 30), (h.BrandB, 20));
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 70));
        var dispatch = Ship(h, order, 70);
        h.Dispatch.Reverse(dispatch.Id);

        var before = Snapshot(h);
        foreach (var b in h.Db.StockBalances) { b.RawOnHand = -999; b.PackedOnHand = -999; b.Reserved = -999; }
        h.Db.SaveChanges();

        h.Stock.ReconcileAll();

        Assert.Equal(before, Snapshot(h));
    }

    [Fact]
    public void Goods_put_back_can_be_shipped_again()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 50, h.BrandA);
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 50));
        var dispatch = Ship(h, order, 50);
        h.Dispatch.Reverse(dispatch.Id);

        Ship(h, order, 50);

        Assert.Equal(50, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
        Assert.Equal(0, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
        Assert.Equal(OrderStatus.Dispatched, Status(h, order.Id));
    }

    // ---- Helpers ---------------------------------------------------------------

    private static OrderInput Line(TestHarness h, int gradeId, int brandId, decimal qty) =>
        new(h.PartyX, DateTime.Today, null,
            new[] { new OrderLineInput(h.ItemA, gradeId, h.White, brandId, qty) },
            Array.Empty<OrderAccessoryLineInput>());

    private static DispatchEntry Ship(TestHarness h, Order order, decimal qty)
    {
        var lineId = h.Db.OrderLines.AsNoTracking()
            .Where(l => l.OrderId == order.Id && l.QuantityOrdered > 0)
            .OrderBy(l => l.Id).First().Id;
        return h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lineId, qty) }, Array.Empty<DispatchAccessoryLineInput>()));
    }

    private static OrderStatus Status(TestHarness h, int orderId) =>
        h.Db.Orders.AsNoTracking().Single(o => o.Id == orderId).Status;

    /// <summary>How much of each allocation has been consumed, keyed by priority.</summary>
    private static Dictionary<int, decimal> Consumed(TestHarness h, int orderLineId) =>
        h.Db.OrderLineAllocations.AsNoTracking()
            .Where(a => a.OrderLineId == orderLineId)
            .ToDictionary(a => a.Priority, a => a.QuantityDispatched);

    private static Dictionary<(int, int, int, int?), (decimal Raw, decimal Packed, decimal Reserved)> Snapshot(TestHarness h) =>
        h.Db.StockBalances.AsNoTracking().ToList()
            .ToDictionary(b => (b.ItemId, b.GradeId, b.ColourId, b.BrandId),
                          b => (b.RawOnHand, b.PackedOnHand, b.Reserved));
}
