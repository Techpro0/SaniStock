using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;
using Xunit;

namespace SaniStock.Domain.Tests;

/// <summary>
/// Covers Part 2: an item's default accessories are auto-reserved when the item is booked,
/// per-line exclusion skips a reservation, dispatch deducts item + accessory stock together,
/// and cancel releases both.
/// </summary>
public class AccessoryBundlingTests
{
    // ---- Booking auto-reserves the item's default accessories ----------------

    [Fact]
    public void Booking_item_auto_reserves_its_default_accessories_scaled_by_qty()
    {
        using var h = new TestHarness();
        // Recipe: 1x Pillar Cock and 2x Seat Cover per item unit.
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m), (h.Acc2, 2m) });

        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 10));

        // Two accessory lines were auto-created, each tied to the item line.
        var itemLineId = order.Lines.Single().Id;
        Assert.Equal(2, order.AccessoryLines.Count);
        Assert.All(order.AccessoryLines, a => Assert.Equal(itemLineId, a.SourceOrderLineId));

        var cock = order.AccessoryLines.Single(a => a.AccessoryId == h.Acc1);
        var seat = order.AccessoryLines.Single(a => a.AccessoryId == h.Acc2);
        Assert.Equal(10, cock.QuantityOrdered); // 1 x 10
        Assert.Equal(20, seat.QuantityOrdered); // 2 x 10

        // The reservation went through the ledger onto the accessory balances.
        Assert.Equal((0, 10, -10), h.AccessoryBalance(h.Acc1));
        Assert.Equal((0, 20, -20), h.AccessoryBalance(h.Acc2));
        // Item reserved as usual.
        Assert.Equal(10, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
    }

    [Fact]
    public void Item_without_defaults_books_with_no_accessory_lines()
    {
        using var h = new TestHarness();
        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 5));
        Assert.Empty(order.AccessoryLines);
    }

    // ---- Per-line exclude skips that accessory's reservation -----------------

    [Fact]
    public void Excluding_an_accessory_line_skips_only_its_reservation()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m), (h.Acc2, 1m) });

        // Book the item but exclude the Seat Cover (Acc2) for this order line.
        var input = new OrderInput(h.PartyX, DateTime.Today, null,
            new[] { new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandA, 12, new[] { h.Acc2 }) },
            Array.Empty<OrderAccessoryLineInput>());
        var order = h.Orders.Book(input);

        Assert.Single(order.AccessoryLines);
        Assert.Equal(h.Acc1, order.AccessoryLines.Single().AccessoryId);

        Assert.Equal((0, 12, -12), h.AccessoryBalance(h.Acc1)); // included → reserved
        Assert.Equal((0, 0, 0), h.AccessoryBalance(h.Acc2));    // excluded → untouched
    }

    [Fact]
    public void Exclusion_is_per_order_and_leaves_the_master_recipe_intact()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m), (h.Acc2, 1m) });

        // First order excludes Acc2...
        h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            new[] { new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandA, 4, new[] { h.Acc2 }) },
            Array.Empty<OrderAccessoryLineInput>()));

        // ...the next order (no exclusions) still gets both — the recipe was not mutated.
        var second = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 4));
        Assert.Equal(2, second.AccessoryLines.Count);
        Assert.Equal(2, h.Master.GetItemAccessoryDefaults(h.ItemA).Count);
    }

    // ---- Dispatch deducts item + accessory together --------------------------

    [Fact]
    public void Dispatch_deducts_item_and_linked_accessory_stock_together()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m) });
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));

        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 30));
        var itemLineId = order.Lines.Single().Id;
        var accLineId = order.AccessoryLines.Single().Id;

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(itemLineId, 30) },
            new[] { new DispatchAccessoryLineInput(accLineId, 30) }));

        Assert.Equal((70m, 0m, 70m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
        Assert.Equal((70m, 0m, 70m), h.AccessoryBalance(h.Acc1));
        Assert.Equal(OrderStatus.Dispatched, h.Db.Orders.Find(order.Id)!.Status);
    }

    [Fact]
    public void Partial_dispatch_of_bundled_accessory_respects_pending()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m) });
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 30));
        var accLineId = order.AccessoryLines.Single().Id;

        // Cannot over-ship the accessory beyond its pending 30.
        Assert.Throws<DomainException>(() => h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(), new[] { new DispatchAccessoryLineInput(accLineId, 31) })));

        // Partial accessory dispatch works and leaves the rest reserved.
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(), new[] { new DispatchAccessoryLineInput(accLineId, 18) }));
        Assert.Equal((82m, 12m, 70m), h.AccessoryBalance(h.Acc1)); // 100-18 onhand, 30-18 reserved
    }

    // ---- Cancel releases both ------------------------------------------------

    [Fact]
    public void Cancel_releases_reservation_for_item_and_bundled_accessories()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m), (h.Acc2, 2m) });
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc2, 100, null));

        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 20));
        Assert.Equal(20, h.AccessoryBalance(h.Acc1).Reserved);
        Assert.Equal(40, h.AccessoryBalance(h.Acc2).Reserved);

        h.Orders.Cancel(order.Id, "customer withdrew");

        Assert.Equal((100m, 0m, 100m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
        Assert.Equal((100m, 0m, 100m), h.AccessoryBalance(h.Acc1));
        Assert.Equal((100m, 0m, 100m), h.AccessoryBalance(h.Acc2));
        Assert.Equal(OrderStatus.Cancelled, h.Db.Orders.Find(order.Id)!.Status);
    }

    // ---- Brand inheritance -----------------------------------------------------
    //
    // The reason brand exists on an accessory line at all: an auto-bundled accessory takes its
    // parent item line's brand, so its reservation prefers that brand's packed accessory stock —
    // not the shared unbranded pool — the same way a manually-added item line already would.

    [Fact]
    public void Auto_attached_accessory_line_inherits_its_parent_item_lines_brand()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m) });

        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandB, 10));

        var accLine = order.AccessoryLines.Single();
        Assert.Equal(h.BrandB, accLine.BrandId);
    }

    [Fact]
    public void Auto_attached_accessory_reservation_prefers_its_brands_packed_stock_over_the_pool()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m) });
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessory(h.Acc1, 40, h.BrandA);

        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 10));

        // The recipe reserved 10 of Acc1 for this line — drawn from Brand A's packed stock, not the
        // 60 still sitting unbranded in the pool.
        var accLine = order.AccessoryLines.Single();
        Assert.Equal(new[] { ((int?)h.BrandA, StockBucket.Packed, 10m) }, h.AccessoryAllocation(accLine.Id));
        Assert.Equal(10, h.AccessoryBrandRow(h.Acc1, h.BrandA).Reserved);
        Assert.Equal(0, h.AccessoryBrandRow(h.Acc1, null).Reserved);
    }

    [Fact]
    public void Auto_attached_accessory_falls_back_to_the_shared_pool_once_its_brands_packed_stock_runs_out()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m) });
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessory(h.Acc1, 6, h.BrandA);   // only 6 packed for Brand A; 94 still unpacked

        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 10));

        var accLine = order.AccessoryLines.Single();
        Assert.Equal(new[]
        {
            ((int?)h.BrandA, StockBucket.Packed, 6m),
            ((int?)null, StockBucket.Raw, 4m),
        }, h.AccessoryAllocation(accLine.Id));
    }

    [Fact]
    public void Another_brands_packed_accessory_stock_is_never_drawn_for_this_lines_brand()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m) });
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessory(h.Acc1, 100, h.BrandB);   // all of it packed under the other brand

        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 10));

        // Nothing is available to Brand A — Brand B's boxes are the wrong boxes — so it is a shortfall.
        var accLine = order.AccessoryLines.Single();
        Assert.Equal(new[] { ((int?)null, StockBucket.Shortfall, 10m) }, h.AccessoryAllocation(accLine.Id));
        Assert.Equal(0, h.AccessoryBrandRow(h.Acc1, h.BrandB).Reserved);
    }

    [Fact]
    public void Dispatch_of_an_auto_attached_accessory_draws_its_brands_packed_stock_first()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m) });
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessory(h.Acc1, 30, h.BrandA);

        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 20));
        var itemLineId = order.Lines.Single().Id;
        var accLineId = order.AccessoryLines.Single().Id;

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(itemLineId, 20) },
            new[] { new DispatchAccessoryLineInput(accLineId, 20) }));

        // 20 of Acc1 shipped out of Brand A's packed 30, leaving 10 packed and the pool untouched.
        Assert.Equal((0m, 10m, 0m), h.AccessoryBrandRow(h.Acc1, h.BrandA));
        Assert.Equal((70m, 0m, 0m), h.AccessoryBrandRow(h.Acc1, null));
    }

    // ---- Recipe editing ------------------------------------------------------

    [Fact]
    public void SetItemAccessoryDefaults_replaces_the_previous_recipe()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m), (h.Acc2, 2m) });
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc2, 5m) }); // replace

        var defaults = h.Master.GetItemAccessoryDefaults(h.ItemA);
        Assert.Equal(new[] { h.Acc2 }, defaults.Select(d => d.AccessoryId).ToArray());
        Assert.Equal(5m, defaults.Single().QtyPerUnit);

        // Clearing removes the recipe entirely.
        h.Master.SetItemAccessoryDefaults(h.ItemA, Array.Empty<(int, decimal)>());
        Assert.Empty(h.Master.GetItemAccessoryDefaults(h.ItemA));
    }

    [Fact]
    public void SetItemAccessoryDefaults_rejects_duplicate_or_nonpositive_qty()
    {
        using var h = new TestHarness();
        Assert.Throws<DomainException>(() =>
            h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m), (h.Acc1, 2m) }));
        Assert.Throws<DomainException>(() =>
            h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 0m) }));
    }

    // ---- Helpers -------------------------------------------------------------

    private static OrderInput SingleLineOrder(int partyId, int itemId, int gradeId, int colourId,
        int brandId, decimal qty) =>
        new(partyId, DateTime.Today, null,
            new[] { new OrderLineInput(itemId, gradeId, colourId, brandId, qty) },
            Array.Empty<OrderAccessoryLineInput>());
}
