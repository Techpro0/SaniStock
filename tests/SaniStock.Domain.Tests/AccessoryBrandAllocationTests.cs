using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;
using Xunit;

namespace SaniStock.Domain.Tests;

/// <summary>
/// Covers the brand dimension for accessories — the accessory analogue of
/// <see cref="BrandAllocationTests"/>. The rule under test is the same, minus a grade chain:
/// accessory booking draws this line's brand's packed stock first, then falls back to the shared
/// unpacked pool, then a shortfall. There is no grade to borrow from below.
/// </summary>
public class AccessoryBrandAllocationTests
{
    // ---- Packing assigns brand -----------------------------------------------

    [Fact]
    public void Receipt_stays_brandless_and_packing_is_what_assigns_a_brand()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));

        Assert.Equal((100m, 0m, 0m), h.AccessoryBrandRow(h.Acc1, null));
        Assert.Equal(0, h.PackedForAccessory(h.Acc1, h.BrandA));

        h.PackAccessory(h.Acc1, 40, h.BrandA);

        Assert.Equal((60m, 0m, 0m), h.AccessoryBrandRow(h.Acc1, null));
        Assert.Equal((0m, 40m, 0m), h.AccessoryBrandRow(h.Acc1, h.BrandA));
        Assert.Equal((60m, 40m), h.AccessoryBuckets(h.Acc1));
        Assert.Equal(100, h.AccessoryBalance(h.Acc1).OnHand);
    }

    [Fact]
    public void One_packing_action_splits_across_several_brands()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 500, null));

        var entries = h.PackAccessorySplit(h.Acc1, (h.BrandA, 200), (h.BrandB, 200), (h.BrandC, 100));

        Assert.Equal(3, entries.Count);
        Assert.Equal(200, h.PackedForAccessory(h.Acc1, h.BrandA));
        Assert.Equal(200, h.PackedForAccessory(h.Acc1, h.BrandB));
        Assert.Equal(100, h.PackedForAccessory(h.Acc1, h.BrandC));
        Assert.Equal((0m, 500m), h.AccessoryBuckets(h.Acc1));

        Assert.Single(entries.Select(e => e.BatchId).Distinct());
        Assert.Equal(entries[0].Id, entries[0].BatchId);
    }

    [Fact]
    public void The_brand_lines_are_checked_against_the_unpacked_balance_as_a_group()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 500, null));

        Assert.Throws<DomainException>(() => h.PackAccessorySplit(h.Acc1, (h.BrandA, 300), (h.BrandB, 300)));

        Assert.Equal((500m, 0m), h.AccessoryBuckets(h.Acc1));
        Assert.Empty(h.Db.AccessoryPackingEntries);
    }

    [Fact]
    public void The_same_brand_cannot_appear_twice_in_one_packing()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 500, null));

        Assert.Throws<DomainException>(() => h.PackAccessorySplit(h.Acc1, (h.BrandA, 100), (h.BrandA, 100)));
        Assert.Empty(h.Db.AccessoryPackingEntries);
    }

    [Fact]
    public void Packing_under_an_unknown_brand_is_rejected()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));

        Assert.Throws<DomainException>(() => h.PackAccessory(h.Acc1, 10, brandId: 9999));
    }

    // ---- Per-brand reversal ---------------------------------------------------

    [Fact]
    public void Undoing_one_brands_portion_leaves_the_other_brands_alone()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 500, null));
        var entries = h.PackAccessorySplit(h.Acc1, (h.BrandA, 200), (h.BrandB, 200), (h.BrandC, 100));

        h.AccessoryPacking.Reverse(entries[1].Id);   // undo Brand B only

        Assert.Equal(200, h.PackedForAccessory(h.Acc1, h.BrandA));
        Assert.Equal(0, h.PackedForAccessory(h.Acc1, h.BrandB));
        Assert.Equal(100, h.PackedForAccessory(h.Acc1, h.BrandC));
        Assert.Equal((200m, 300m), h.AccessoryBuckets(h.Acc1));
    }

    [Fact]
    public void Reversal_is_blocked_only_for_the_brand_that_has_shipped_since()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 400, null));
        var entries = h.PackAccessorySplit(h.Acc1, (h.BrandA, 200), (h.BrandB, 200));

        var order = h.Orders.Book(Line(h, h.BrandA, 50));
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(),
            new[] { new DispatchAccessoryLineInput(order.AccessoryLines.Single().Id, 50) }));

        Assert.Throws<DomainException>(() => h.AccessoryPacking.Reverse(entries[0].Id));
        h.AccessoryPacking.Reverse(entries[1].Id);

        Assert.Equal(150, h.PackedForAccessory(h.Acc1, h.BrandA));
        Assert.Equal(0, h.PackedForAccessory(h.Acc1, h.BrandB));
    }

    // ---- Booking: packed steps are brand-scoped, unpacked steps are not -------

    [Fact]
    public void Packed_stock_of_the_lines_own_brand_is_drawn_first()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessory(h.Acc1, 50, h.BrandA);

        var order = h.Orders.Book(Line(h, h.BrandA, 30));

        Assert.Equal(new[] { ((int?)h.BrandA, StockBucket.Packed, 30m) },
            h.AccessoryAllocation(order.AccessoryLines.Single().Id));
        Assert.Equal(30, h.AccessoryBrandRow(h.Acc1, h.BrandA).Reserved);
        Assert.Equal(0, h.AccessoryBrandRow(h.Acc1, null).Reserved);
    }

    [Fact]
    public void Another_brands_packed_stock_is_never_drawn()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessory(h.Acc1, 100, h.BrandB);

        var order = h.Orders.Book(Line(h, h.BrandA, 40));

        Assert.Equal(new[] { ((int?)null, StockBucket.Shortfall, 40m) },
            h.AccessoryAllocation(order.AccessoryLines.Single().Id));
        Assert.Equal(0, h.AccessoryBrandRow(h.Acc1, h.BrandB).Reserved);
    }

    [Fact]
    public void Unpacked_stock_is_shared_and_covers_an_order_for_any_brand()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessory(h.Acc1, 60, h.BrandB);

        var order = h.Orders.Book(Line(h, h.BrandA, 40));

        Assert.Equal(new[] { ((int?)null, StockBucket.Raw, 40m) },
            h.AccessoryAllocation(order.AccessoryLines.Single().Id));
        Assert.Equal(40, h.AccessoryBrandRow(h.Acc1, null).Reserved);
    }

    [Fact]
    public void Two_lines_for_different_brands_do_not_compete_for_each_others_packed_stock()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessorySplit(h.Acc1, (h.BrandA, 50), (h.BrandB, 50));

        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            Array.Empty<OrderLineInput>(),
            new[]
            {
                new OrderAccessoryLineInput(h.Acc1, h.BrandA, 50),
                new OrderAccessoryLineInput(h.Acc1, h.BrandB, 50),
            }));

        var lines = order.AccessoryLines.ToList();
        Assert.Equal(new[] { ((int?)h.BrandA, StockBucket.Packed, 50m) }, h.AccessoryAllocation(lines[0].Id));
        Assert.Equal(new[] { ((int?)h.BrandB, StockBucket.Packed, 50m) }, h.AccessoryAllocation(lines[1].Id));
        Assert.Equal(100, h.AccessoryBalance(h.Acc1).Reserved);
        Assert.Equal(0, h.AccessoryBalance(h.Acc1).Available);
    }

    [Fact]
    public void Two_brands_booking_against_the_same_pool_see_each_others_claims()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 40, null));

        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            Array.Empty<OrderLineInput>(),
            new[]
            {
                new OrderAccessoryLineInput(h.Acc1, h.BrandA, 30),
                new OrderAccessoryLineInput(h.Acc1, h.BrandB, 30),
            }));
        var lines = order.AccessoryLines.ToList();

        Assert.Equal(new[] { ((int?)null, StockBucket.Raw, 30m) }, h.AccessoryAllocation(lines[0].Id));
        Assert.Equal(new[]
        {
            ((int?)null, StockBucket.Raw, 10m),
            ((int?)null, StockBucket.Shortfall, 20m),
        }, h.AccessoryAllocation(lines[1].Id));

        Assert.Equal(60, h.AccessoryBalance(h.Acc1).Reserved);
        Assert.Equal(-20, h.AccessoryBalance(h.Acc1).Available);
    }

    // ---- Dispatch: the recorded source is preferred, its sibling is the fallback

    [Fact]
    public void Stock_booked_while_unpacked_can_still_ship_after_it_is_packed_under_that_brand()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        var order = h.Orders.Book(Line(h, h.BrandA, 30));
        Assert.Equal(new[] { ((int?)null, StockBucket.Raw, 30m) },
            h.AccessoryAllocation(order.AccessoryLines.Single().Id));

        h.PackAccessory(h.Acc1, 100, h.BrandA);

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(),
            new[] { new DispatchAccessoryLineInput(order.AccessoryLines.Single().Id, 30) }));

        Assert.Equal((0m, 70m, 0m), h.AccessoryBrandRow(h.Acc1, h.BrandA));
        Assert.Equal((0m, 0m, 0m), h.AccessoryBrandRow(h.Acc1, null));
        Assert.Equal(70, h.AccessoryBalance(h.Acc1).OnHand);
    }

    [Fact]
    public void Stock_booked_as_packed_can_still_ship_after_the_packing_is_undone()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        var packing = h.PackAccessory(h.Acc1, 50, h.BrandA);
        var order = h.Orders.Book(Line(h, h.BrandA, 50));
        Assert.Equal(new[] { ((int?)h.BrandA, StockBucket.Packed, 50m) },
            h.AccessoryAllocation(order.AccessoryLines.Single().Id));

        h.AccessoryPacking.Reverse(packing.Id);

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(),
            new[] { new DispatchAccessoryLineInput(order.AccessoryLines.Single().Id, 50) }));

        Assert.Equal((50m, 0m, 0m), h.AccessoryBrandRow(h.Acc1, null));
        Assert.Equal((0m, 0m, 0m), h.AccessoryBrandRow(h.Acc1, h.BrandA));
    }

    [Fact]
    public void Dispatch_will_not_reach_into_another_brands_packed_stock_to_cover_a_shortfall()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        var order = h.Orders.Book(Line(h, h.BrandA, 60));

        h.PackAccessory(h.Acc1, 100, h.BrandB);

        var ex = Assert.Throws<DomainException>(() => h.Dispatch.Dispatch(
            new DispatchInput(order.Id, DateTime.Today, null,
                Array.Empty<DispatchLineInput>(),
                new[] { new DispatchAccessoryLineInput(order.AccessoryLines.Single().Id, 60) })));
        Assert.Contains("Add stock first", ex.Message);

        Assert.Equal(100, h.PackedForAccessory(h.Acc1, h.BrandB));
        Assert.Equal(60, h.AccessoryBrandRow(h.Acc1, null).Reserved);
    }

    [Fact]
    public void Dispatch_spanning_both_rows_releases_each_reservation_from_the_row_holding_it()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessory(h.Acc1, 30, h.BrandA);
        var order = h.Orders.Book(Line(h, h.BrandA, 90));

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(),
            new[] { new DispatchAccessoryLineInput(order.AccessoryLines.Single().Id, 90) }));

        Assert.Equal((10m, 0m, 0m), h.AccessoryBrandRow(h.Acc1, null));
        Assert.Equal((0m, 0m, 0m), h.AccessoryBrandRow(h.Acc1, h.BrandA));
        Assert.Equal(10, h.AccessoryBalance(h.Acc1).OnHand);
    }

    [Fact]
    public void Two_brands_drawing_on_the_same_pool_in_one_dispatch_cannot_together_overdraw_it()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 40, null));

        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            Array.Empty<OrderLineInput>(),
            new[]
            {
                new OrderAccessoryLineInput(h.Acc1, h.BrandA, 30),
                new OrderAccessoryLineInput(h.Acc1, h.BrandB, 30),
            }));
        var lines = order.AccessoryLines.ToList();

        Assert.Throws<DomainException>(() => h.Dispatch.Dispatch(
            new DispatchInput(order.Id, DateTime.Today, null,
                Array.Empty<DispatchLineInput>(),
                new[]
                {
                    new DispatchAccessoryLineInput(lines[0].Id, 30),
                    new DispatchAccessoryLineInput(lines[1].Id, 30),
                })));

        Assert.Equal((40m, 0m), h.AccessoryBuckets(h.Acc1));

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(),
            new[]
            {
                new DispatchAccessoryLineInput(lines[0].Id, 30),
                new DispatchAccessoryLineInput(lines[1].Id, 10),
            }));
        Assert.Equal(0, h.AccessoryBrandRow(h.Acc1, null).Raw);
    }

    // ---- Cancellation ---------------------------------------------------------

    [Fact]
    public void Cancelling_releases_each_source_row_in_reverse_draw_order()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessory(h.Acc1, 30, h.BrandA);
        var order = h.Orders.Book(Line(h, h.BrandA, 90));
        Assert.Equal(30, h.AccessoryBrandRow(h.Acc1, h.BrandA).Reserved);
        Assert.Equal(60, h.AccessoryBrandRow(h.Acc1, null).Reserved);

        h.Orders.Cancel(order.Id, "customer withdrew");

        Assert.Equal((70m, 0m, 0m), h.AccessoryBrandRow(h.Acc1, null));
        Assert.Equal((0m, 30m, 0m), h.AccessoryBrandRow(h.Acc1, h.BrandA));
        Assert.Equal(100, h.AccessoryBalance(h.Acc1).Available);
    }

    [Fact]
    public void Cancelling_one_brands_order_frees_nothing_of_another_brands()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessorySplit(h.Acc1, (h.BrandA, 50), (h.BrandB, 50));
        var a = h.Orders.Book(Line(h, h.BrandA, 50));
        h.Orders.Book(Line(h, h.BrandB, 50));

        h.Orders.Cancel(a.Id);

        Assert.Equal(0, h.AccessoryBrandRow(h.Acc1, h.BrandA).Reserved);
        Assert.Equal(50, h.AccessoryBrandRow(h.Acc1, h.BrandB).Reserved);
    }

    // ---- Reconciliation -------------------------------------------------------

    [Fact]
    public void Reconcile_rebuilds_the_per_brand_split_from_the_ledger()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 500, null));
        h.PackAccessorySplit(h.Acc1, (h.BrandA, 200), (h.BrandB, 150));
        var order = h.Orders.Book(Line(h, h.BrandA, 250));
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(),
            new[] { new DispatchAccessoryLineInput(order.AccessoryLines.Single().Id, 220) }));

        var before = Snapshot(h);
        foreach (var b in h.Db.AccessoryStockBalances) { b.RawOnHand = -999; b.PackedOnHand = -999; b.Reserved = -999; }
        h.Db.SaveChanges();

        h.Stock.ReconcileAll();

        Assert.Equal(before, Snapshot(h));
        Assert.Equal(150, h.PackedForAccessory(h.Acc1, h.BrandB));
    }

    // ---- The invariant that keeps the two rows meaningful ---------------------

    [Fact]
    public void A_brandless_movement_cannot_carry_packed_quantity()
    {
        using var h = new TestHarness();
        Assert.Throws<DomainException>(() => h.Stock.ApplyAccessory(
            h.Acc1, brandId: null, StockMovementType.Packing,
            deltaRawOnHand: 0, deltaPackedOnHand: 10, deltaReserved: 0,
            DateTime.Today, null, null, null));
    }

    [Fact]
    public void A_branded_movement_cannot_carry_unpacked_quantity()
    {
        using var h = new TestHarness();
        Assert.Throws<DomainException>(() => h.Stock.ApplyAccessory(
            h.Acc1, h.BrandA, StockMovementType.Production,
            deltaRawOnHand: 10, deltaPackedOnHand: 0, deltaReserved: 0,
            DateTime.Today, null, null, null));
    }

    [Fact]
    public void Both_rows_may_hold_reservations()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessory(h.Acc1, 40, h.BrandA);

        h.Orders.Book(Line(h, h.BrandA, 80));

        Assert.Equal(40, h.AccessoryBrandRow(h.Acc1, h.BrandA).Reserved);
        Assert.Equal(40, h.AccessoryBrandRow(h.Acc1, null).Reserved);
    }

    // ---- Helpers -------------------------------------------------------------

    private static OrderInput Line(TestHarness h, int brandId, decimal qty) =>
        new(h.PartyX, DateTime.Today, null,
            Array.Empty<OrderLineInput>(),
            new[] { new OrderAccessoryLineInput(h.Acc1, brandId, qty) });

    private static Dictionary<(int, int?), (decimal Raw, decimal Packed, decimal Reserved)> Snapshot(TestHarness h) =>
        h.Db.AccessoryStockBalances.AsQueryable().ToList()
            .ToDictionary(b => (b.AccessoryId, b.BrandId),
                          b => (b.RawOnHand, b.PackedOnHand, b.Reserved));
}
