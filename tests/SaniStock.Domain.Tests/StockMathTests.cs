using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;
using Xunit;

namespace SaniStock.Domain.Tests;

public class StockMathTests
{
    // ---- Production ----------------------------------------------------------

    [Fact]
    public void Production_increases_onhand_and_creates_combination()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 50, null));

        var bal = h.FinishedBalance(h.ItemA, h.Grade1, h.White);
        Assert.Equal(50, bal.OnHand);
        Assert.Equal(0, bal.Reserved);
        Assert.Equal(50, bal.Available);
    }

    [Fact]
    public void Production_reversal_decreases_onhand_and_leaves_original()
    {
        using var h = new TestHarness();
        var entry = h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 50, null));
        h.Production.Reverse(entry.Id);

        Assert.Equal(0, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
        Assert.Equal(50, h.Db.ProductionEntries.Find(entry.Id)!.Quantity); // original untouched
        Assert.Equal(2, h.Db.ProductionEntries.Count());
    }

    [Fact]
    public void Production_reversal_twice_is_blocked()
    {
        using var h = new TestHarness();
        var entry = h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 50, null));
        h.Production.Reverse(entry.Id);
        Assert.Throws<DomainException>(() => h.Production.Reverse(entry.Id));
    }

    [Fact]
    public void Production_nonpositive_quantity_is_rejected()
    {
        using var h = new TestHarness();
        Assert.Throws<DomainException>(() =>
            h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 0, null)));
    }

    // ---- Booking / reservation ----------------------------------------------

    [Fact]
    public void Booking_increases_reserved_but_not_onhand()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 30));

        var bal = h.FinishedBalance(h.ItemA, h.Grade1, h.White);
        Assert.Equal(100, bal.OnHand);
        Assert.Equal(30, bal.Reserved);
        Assert.Equal(70, bal.Available);
    }

    [Fact]
    public void Booking_is_never_blocked_and_may_drive_available_negative()
    {
        using var h = new TestHarness();
        // No production at all; booking should still succeed and signal shortfall.
        h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 40));

        var bal = h.FinishedBalance(h.ItemA, h.Grade1, h.White);
        Assert.Equal(0, bal.OnHand);
        Assert.Equal(40, bal.Reserved);
        Assert.Equal(-40, bal.Available);
    }

    [Fact]
    public void Booking_two_orders_same_key_aggregates_reservation()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 30));
        h.Orders.Book(SingleLineOrder(h.PartyY, h.ItemA, h.Grade1, h.White, h.BrandA, 25));

        var bal = h.FinishedBalance(h.ItemA, h.Grade1, h.White);
        Assert.Equal(55, bal.Reserved);
        Assert.Equal(45, bal.Available);
    }

    [Fact]
    public void Booking_empty_order_is_rejected()
    {
        using var h = new TestHarness();
        var empty = new OrderInput(h.PartyX, DateTime.Today, null,
            Array.Empty<OrderLineInput>(), Array.Empty<OrderAccessoryLineInput>());
        Assert.Throws<DomainException>(() => h.Orders.Book(empty));
    }

    // ---- Dispatch ------------------------------------------------------------

    [Fact]
    public void Dispatch_decreases_onhand_and_reserved_together()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 30));
        var line = order.Lines.Single();

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(line.Id, 20) }, Array.Empty<DispatchAccessoryLineInput>()));

        var bal = h.FinishedBalance(h.ItemA, h.Grade1, h.White);
        Assert.Equal(80, bal.OnHand);   // 100 - 20
        Assert.Equal(10, bal.Reserved); // 30 - 20
        Assert.Equal(70, bal.Available);
    }

    [Fact]
    public void Dispatch_cannot_exceed_pending()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 30));
        var line = order.Lines.Single();

        Assert.Throws<DomainException>(() => h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(line.Id, 31) }, Array.Empty<DispatchAccessoryLineInput>())));

        // Nothing should have changed after the rejected dispatch.
        var bal = h.FinishedBalance(h.ItemA, h.Grade1, h.White);
        Assert.Equal(100, bal.OnHand);
        Assert.Equal(30, bal.Reserved);
    }

    [Fact]
    public void Partial_then_final_dispatch_has_no_double_deduction_and_updates_status()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 30));
        var lineId = order.Lines.Single().Id;

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lineId, 18) }, Array.Empty<DispatchAccessoryLineInput>()));
        Assert.Equal(OrderStatus.PartiallyDispatched, h.Db.Orders.Find(order.Id)!.Status);

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lineId, 12) }, Array.Empty<DispatchAccessoryLineInput>()));

        var bal = h.FinishedBalance(h.ItemA, h.Grade1, h.White);
        Assert.Equal(70, bal.OnHand);  // 100 - 30 total, deducted exactly once
        Assert.Equal(0, bal.Reserved); // fully shipped
        Assert.Equal(OrderStatus.Dispatched, h.Db.Orders.Find(order.Id)!.Status);
    }

    [Fact]
    public void Dispatch_is_blocked_when_stock_not_yet_produced()
    {
        using var h = new TestHarness();
        // Booked 30 but only 20 produced so far.
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 20, null));
        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 30));
        var lineId = order.Lines.Single().Id;

        // Cannot ship 25 — only 20 physically in stock.
        Assert.Throws<DomainException>(() => h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lineId, 25) }, Array.Empty<DispatchAccessoryLineInput>())));

        // Nothing changed after the blocked dispatch.
        var bal = h.FinishedBalance(h.ItemA, h.Grade1, h.White);
        Assert.Equal(20, bal.OnHand);
        Assert.Equal(30, bal.Reserved);

        // Shipping the 20 that exist is allowed; the rest stays pending.
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lineId, 20) }, Array.Empty<DispatchAccessoryLineInput>()));
        bal = h.FinishedBalance(h.ItemA, h.Grade1, h.White);
        Assert.Equal(0, bal.OnHand);
        Assert.Equal(10, bal.Reserved);
        Assert.Equal(OrderStatus.PartiallyDispatched, h.Db.Orders.Find(order.Id)!.Status);
    }

    [Fact]
    public void Dispatch_is_blocked_when_no_stock_produced_at_all()
    {
        using var h = new TestHarness();
        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 10));
        var lineId = order.Lines.Single().Id;
        Assert.Throws<DomainException>(() => h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lineId, 10) }, Array.Empty<DispatchAccessoryLineInput>())));
    }

    [Fact]
    public void Accessory_dispatch_is_blocked_when_stock_insufficient()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 30, null));
        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            Array.Empty<OrderLineInput>(), new[] { new OrderAccessoryLineInput(h.Acc1, 50) }));
        var accLineId = order.AccessoryLines.Single().Id;

        Assert.Throws<DomainException>(() => h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(), new[] { new DispatchAccessoryLineInput(accLineId, 40) })));
    }

    // ---- Cancellation --------------------------------------------------------

    [Fact]
    public void Cancel_releases_reservation()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 30));

        h.Orders.Cancel(order.Id, "customer withdrew");

        var bal = h.FinishedBalance(h.ItemA, h.Grade1, h.White);
        Assert.Equal(100, bal.OnHand);
        Assert.Equal(0, bal.Reserved);
        Assert.Equal(100, bal.Available);
        Assert.Equal(OrderStatus.Cancelled, h.Db.Orders.Find(order.Id)!.Status);
    }

    [Fact]
    public void Cancel_after_partial_dispatch_releases_only_the_remaining_reservation()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 30));
        var lineId = order.Lines.Single().Id;

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lineId, 20) }, Array.Empty<DispatchAccessoryLineInput>()));
        h.Orders.Cancel(order.Id);

        var bal = h.FinishedBalance(h.ItemA, h.Grade1, h.White);
        Assert.Equal(80, bal.OnHand);   // 20 shipped
        Assert.Equal(0, bal.Reserved);  // remaining 10 released
        Assert.Equal(80, bal.Available);
    }

    [Fact]
    public void Cancel_fully_dispatched_order_is_blocked()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 30));
        var lineId = order.Lines.Single().Id;
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lineId, 30) }, Array.Empty<DispatchAccessoryLineInput>()));

        Assert.Throws<DomainException>(() => h.Orders.Cancel(order.Id));
    }

    // ---- Accessories ---------------------------------------------------------

    [Fact]
    public void Accessory_receipt_book_and_dispatch_flow()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 200, null));

        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            Array.Empty<OrderLineInput>(),
            new[] { new OrderAccessoryLineInput(h.Acc1, 50) }));
        Assert.Equal((200, 50, 150), h.AccessoryBalance(h.Acc1));

        var accLineId = order.AccessoryLines.Single().Id;
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(),
            new[] { new DispatchAccessoryLineInput(accLineId, 50) }));

        Assert.Equal((150, 0, 150), h.AccessoryBalance(h.Acc1));
        Assert.Equal(OrderStatus.Dispatched, h.Db.Orders.Find(order.Id)!.Status);
    }

    // ---- Reconciliation ------------------------------------------------------

    [Fact]
    public void Reconcile_rebuilds_balances_identical_to_incremental()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade2, h.Blue, 40, null));
        var o1 = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 30));
        h.Dispatch.Dispatch(new DispatchInput(o1.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(o1.Lines.Single().Id, 10) }, Array.Empty<DispatchAccessoryLineInput>()));

        var before = SnapshotFinished(h);
        // Corrupt the cached balances, then rebuild from the ledger.
        foreach (var b in h.Db.StockBalances) { b.RawOnHand = -999; b.PackedOnHand = -999; b.Reserved = -999; }
        h.Db.SaveChanges();

        h.Stock.ReconcileAll();
        var after = SnapshotFinished(h);

        Assert.Equal(before, after);
        // Nothing was packed, so it all still sits on the brand-less unpacked row.
        Assert.Equal((90m, 20m), before[(h.ItemA, h.Grade1, h.White, (int?)null)]); // 100-10 onhand, 30-10 reserved
    }

    // ---- Green ware & raw material -------------------------------------------

    [Fact]
    public void Green_and_raw_in_out_track_balance()
    {
        using var h = new TestHarness();
        h.Green.Post(new GreenPieceInput(DateTime.Today, h.ItemA, h.White, IsIssue: false, 60, null));
        h.Green.Post(new GreenPieceInput(DateTime.Today, h.ItemA, h.White, IsIssue: true, 15, null));
        var green = h.Db.GreenPieceBalances.Single(x => x.ItemId == h.ItemA && x.ColourId == h.White);
        Assert.Equal(45, green.OnHand);

        h.Raw.Post(new RawMaterialInput(DateTime.Today, h.RawClay, IsIssue: false, 500, null));
        h.Raw.Post(new RawMaterialInput(DateTime.Today, h.RawClay, IsIssue: true, 120, null));
        var raw = h.Db.RawMaterialBalances.Single(x => x.RawMaterialId == h.RawClay);
        Assert.Equal(380, raw.OnHand);
    }

    // ---- Helpers -------------------------------------------------------------

    private static OrderInput SingleLineOrder(int partyId, int itemId, int gradeId, int colourId,
        int brandId, decimal qty) =>
        new(partyId, DateTime.Today, null,
            new[] { new OrderLineInput(itemId, gradeId, colourId, brandId, qty) },
            Array.Empty<OrderAccessoryLineInput>());

    /// <summary>Keyed per balance row, brand included — that is the grain reconcile works at.</summary>
    private static Dictionary<(int, int, int, int?), (decimal, decimal)> SnapshotFinished(TestHarness h) =>
        h.Db.StockBalances.AsQueryable().ToList()
            .ToDictionary(b => (b.ItemId, b.GradeId, b.ColourId, b.BrandId), b => (b.OnHand, b.Reserved));
}
