using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;
using Xunit;

namespace SaniStock.Domain.Tests;

/// <summary>
/// Covers the accessory packing stage — the accessory analogue of <see cref="PackingMathTests"/>:
/// receipts land in unpacked stock, packing moves quantity from unpacked to packed without changing
/// the total, and both buckets rebuild from the ledger. Accessories have no grade/colour, so there is
/// no cross-grade concern to cover here.
/// </summary>
public class AccessoryPackingTests
{
    [Fact]
    public void Receipt_lands_in_unpacked_stock_only()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 50, null));

        Assert.Equal((50m, 0m), h.AccessoryBuckets(h.Acc1));
        Assert.Equal(50, h.AccessoryBalance(h.Acc1).OnHand);
    }

    [Fact]
    public void Packing_moves_unpacked_to_packed_and_leaves_the_total_alone()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));

        h.PackAccessory(h.Acc1, 30);

        Assert.Equal((70m, 30m), h.AccessoryBuckets(h.Acc1));
        var bal = h.AccessoryBalance(h.Acc1);
        Assert.Equal(100, bal.OnHand);
        Assert.Equal(0, bal.Reserved);
        Assert.Equal(100, bal.Available);
    }

    [Fact]
    public void Packing_more_than_is_unpacked_is_blocked()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 40, null));

        Assert.Throws<DomainException>(() => h.PackAccessory(h.Acc1, 41));

        Assert.Equal((40m, 0m), h.AccessoryBuckets(h.Acc1));
    }

    [Fact]
    public void Packing_with_no_stock_at_all_is_blocked()
    {
        using var h = new TestHarness();
        Assert.Throws<DomainException>(() => h.PackAccessory(h.Acc1, 1));
    }

    [Fact]
    public void Packing_nonpositive_quantity_is_rejected()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 40, null));
        Assert.Throws<DomainException>(() => h.PackAccessory(h.Acc1, 0));
        Assert.Throws<DomainException>(() => h.PackAccessory(h.Acc1, -5));
    }

    [Fact]
    public void Packing_reversal_restores_the_previous_split_and_leaves_the_original()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessory(h.Acc1, 20);
        var entry = h.PackAccessory(h.Acc1, 30);
        Assert.Equal((50m, 50m), h.AccessoryBuckets(h.Acc1));

        h.AccessoryPacking.Reverse(entry.Id);

        Assert.Equal((80m, 20m), h.AccessoryBuckets(h.Acc1));
        Assert.Equal(100, h.AccessoryBalance(h.Acc1).OnHand);
        Assert.Equal(30, h.Db.AccessoryPackingEntries.Find(entry.Id)!.Quantity);
        Assert.Equal(3, h.Db.AccessoryPackingEntries.Count());
    }

    [Fact]
    public void Packing_reversal_twice_is_blocked()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        var entry = h.PackAccessory(h.Acc1, 30);
        h.AccessoryPacking.Reverse(entry.Id);

        Assert.Throws<DomainException>(() => h.AccessoryPacking.Reverse(entry.Id));
    }

    [Fact]
    public void A_packing_reversal_cannot_itself_be_reversed()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        var entry = h.PackAccessory(h.Acc1, 30);
        var reversal = h.AccessoryPacking.Reverse(entry.Id);

        Assert.Throws<DomainException>(() => h.AccessoryPacking.Reverse(reversal.Id));
    }

    [Fact]
    public void Packing_reversal_is_blocked_once_stock_has_shipped_since()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        var entry = h.PackAccessory(h.Acc1, 40, h.BrandA);

        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            Array.Empty<OrderLineInput>(),
            new[] { new OrderAccessoryLineInput(h.Acc1, h.BrandA, 10) }));
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(),
            new[] { new DispatchAccessoryLineInput(order.AccessoryLines.Single().Id, 10) }));

        Assert.Throws<DomainException>(() => h.AccessoryPacking.Reverse(entry.Id));

        // The blocked reversal changed nothing: 10 shipped out of the packed 40.
        Assert.Equal((60m, 30m), h.AccessoryBuckets(h.Acc1));
    }

    [Fact]
    public void Receipt_reversal_is_blocked_once_the_stock_has_been_packed()
    {
        using var h = new TestHarness();
        var entry = h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 50, null));
        h.PackAccessory(h.Acc1, 20);

        // Only 30 is still unpacked, so the receipt of 50 cannot be unwound.
        Assert.Throws<DomainException>(() => h.AccessoryReceipts.Reverse(entry.Id));
        Assert.Equal((30m, 20m), h.AccessoryBuckets(h.Acc1));

        // Undo the packing first and the receipt reversal goes through.
        var packing = h.Db.AccessoryPackingEntries.Single(e => !e.IsReversal);
        h.AccessoryPacking.Reverse(packing.Id);
        h.AccessoryReceipts.Reverse(entry.Id);
        Assert.Equal((0m, 0m), h.AccessoryBuckets(h.Acc1));
    }

    [Fact]
    public void Dispatch_takes_packed_stock_before_unpacked()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessory(h.Acc1, 30, h.BrandA);
        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            Array.Empty<OrderLineInput>(),
            new[] { new OrderAccessoryLineInput(h.Acc1, h.BrandA, 50) }));
        var lineId = order.AccessoryLines.Single().Id;

        // 20 fits inside the packed 30, so nothing unpacked is touched.
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(), new[] { new DispatchAccessoryLineInput(lineId, 20) }));
        Assert.Equal((70m, 10m), h.AccessoryBuckets(h.Acc1));

        // The next 30 exhausts the packed 10 and falls back to unpacked for the other 20.
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(), new[] { new DispatchAccessoryLineInput(lineId, 30) }));
        Assert.Equal((50m, 0m), h.AccessoryBuckets(h.Acc1));
        Assert.Equal(50, h.AccessoryBalance(h.Acc1).OnHand);
    }

    [Fact]
    public void Dispatch_is_still_blocked_when_packed_and_unpacked_together_fall_short()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 20, null));
        h.PackAccessory(h.Acc1, 5, h.BrandA);
        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            Array.Empty<OrderLineInput>(),
            new[] { new OrderAccessoryLineInput(h.Acc1, h.BrandA, 30) }));
        var lineId = order.AccessoryLines.Single().Id;

        Assert.Throws<DomainException>(() => h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(), new[] { new DispatchAccessoryLineInput(lineId, 25) })));

        Assert.Equal((15m, 5m), h.AccessoryBuckets(h.Acc1));
    }

    [Fact]
    public void Reconcile_rebuilds_both_accessory_buckets_independently()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessory(h.Acc1, 60, h.BrandA);
        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            Array.Empty<OrderLineInput>(),
            new[] { new OrderAccessoryLineInput(h.Acc1, h.BrandA, 30) }));
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(),
            new[] { new DispatchAccessoryLineInput(order.AccessoryLines.Single().Id, 25) }));

        var before = Snapshot(h);
        Assert.Equal((40m, 0m, 0m), before[(h.Acc1, null)]);
        Assert.Equal((0m, 35m, 5m), before[(h.Acc1, h.BrandA)]);

        foreach (var b in h.Db.AccessoryStockBalances) { b.RawOnHand = -999; b.PackedOnHand = -999; b.Reserved = -999; }
        h.Db.SaveChanges();

        h.Stock.ReconcileAll();

        Assert.Equal(before, Snapshot(h));
    }

    /// <summary>Mirrors <see cref="PackingMathTests.A_packing_posts_a_brandless_raw_leg_and_a_branded_packed_leg_that_net_to_zero"/>.</summary>
    [Fact]
    public void An_accessory_packing_posts_a_brandless_raw_leg_and_a_branded_packed_leg_that_net_to_zero()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));
        h.PackAccessory(h.Acc1, 40, h.BrandA);

        var legs = h.Db.AccessoryStockMovements.Where(m => m.Type == StockMovementType.Packing).ToList();
        Assert.Equal(2, legs.Count);

        var rawLeg = legs.Single(m => m.BrandId == null);
        Assert.Equal(-40, rawLeg.DeltaRawOnHand);
        Assert.Equal(0, rawLeg.DeltaPackedOnHand);

        var packedLeg = legs.Single(m => m.BrandId == h.BrandA);
        Assert.Equal(0, packedLeg.DeltaRawOnHand);
        Assert.Equal(40, packedLeg.DeltaPackedOnHand);

        Assert.Equal(0, legs.Sum(m => m.DeltaOnHand));
        Assert.Equal(0, legs.Sum(m => m.DeltaReserved));
    }

    private static Dictionary<(int, int?), (decimal Raw, decimal Packed, decimal Reserved)> Snapshot(TestHarness h) =>
        h.Db.AccessoryStockBalances.AsQueryable().ToList()
            .ToDictionary(b => (b.AccessoryId, b.BrandId),
                          b => (b.RawOnHand, b.PackedOnHand, b.Reserved));
}
