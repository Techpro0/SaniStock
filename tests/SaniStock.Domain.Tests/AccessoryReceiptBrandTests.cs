using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;
using Xunit;

namespace SaniStock.Domain.Tests;

/// <summary>
/// Covers receiving accessory stock directly into a brand's packed stock — for goods that arrive
/// already packaged, such as an outside import — instead of always landing in the shared unpacked
/// pool and needing a separate trip to the Packing screen.
/// </summary>
public class AccessoryReceiptBrandTests
{
    [Fact]
    public void Receiving_with_no_brand_lands_in_the_shared_unpacked_pool_as_before()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));

        Assert.Equal((100m, 0m, 0m), h.AccessoryBrandRow(h.Acc1, null));
        Assert.Equal(0, h.PackedForAccessory(h.Acc1, h.BrandA));
    }

    [Fact]
    public void Receiving_with_a_brand_lands_directly_in_that_brands_packed_stock()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null, h.BrandA));

        // Straight onto the brand's packed row; the shared pool never saw it.
        Assert.Equal((0m, 100m, 0m), h.AccessoryBrandRow(h.Acc1, h.BrandA));
        Assert.Equal((0m, 0m, 0m), h.AccessoryBrandRow(h.Acc1, null));
        Assert.Equal(100, h.AccessoryBalance(h.Acc1).OnHand);
    }

    [Fact]
    public void Receiving_under_an_unknown_brand_is_rejected()
    {
        using var h = new TestHarness();
        Assert.Throws<DomainException>(() =>
            h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null, 9999)));

        Assert.Equal((0, 0, 0), h.AccessoryBalance(h.Acc1));
    }

    [Fact]
    public void Two_brands_receiving_the_same_accessory_do_not_interfere_with_each_other_or_the_pool()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 50, null));           // pool
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 30, null, h.BrandA));  // Brand A
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 20, null, h.BrandB));  // Brand B

        Assert.Equal((50m, 0m, 0m), h.AccessoryBrandRow(h.Acc1, null));
        Assert.Equal((0m, 30m, 0m), h.AccessoryBrandRow(h.Acc1, h.BrandA));
        Assert.Equal((0m, 20m, 0m), h.AccessoryBrandRow(h.Acc1, h.BrandB));
        Assert.Equal(100, h.AccessoryBalance(h.Acc1).OnHand);
    }

    [Fact]
    public void Reversing_a_branded_receipt_takes_it_back_out_of_that_brands_packed_stock()
    {
        using var h = new TestHarness();
        var receipt = h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null, h.BrandA));

        h.AccessoryReceipts.Reverse(receipt.Id);

        Assert.Equal((0m, 0m, 0m), h.AccessoryBrandRow(h.Acc1, h.BrandA));
        Assert.Equal((0m, 0m, 0m), h.AccessoryBrandRow(h.Acc1, null));
        Assert.Equal(0, h.AccessoryBalance(h.Acc1).OnHand);
        Assert.Equal(h.BrandA, h.Db.AccessoryReceipts.Find(receipt.Id)!.BrandId);
    }

    [Fact]
    public void Reversing_a_branded_receipt_is_blocked_once_some_of_it_has_shipped()
    {
        using var h = new TestHarness();
        var receipt = h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null, h.BrandA));
        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            Array.Empty<OrderLineInput>(),
            new[] { new OrderAccessoryLineInput(h.Acc1, h.BrandA, 40) }));
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(),
            new[] { new DispatchAccessoryLineInput(order.AccessoryLines.Single().Id, 40) }));

        var ex = Assert.Throws<DomainException>(() => h.AccessoryReceipts.Reverse(receipt.Id));
        Assert.Contains("already gone out", ex.Message);

        // The blocked reversal changed nothing further: 40 shipped out of the packed 100.
        Assert.Equal((0m, 60m, 0m), h.AccessoryBrandRow(h.Acc1, h.BrandA));
    }

    [Fact]
    public void A_branded_receipt_can_still_be_dispatched_immediately_with_no_packing_step()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null, h.BrandA));

        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            Array.Empty<OrderLineInput>(),
            new[] { new OrderAccessoryLineInput(h.Acc1, h.BrandA, 30) }));
        var lineId = order.AccessoryLines.Single().Id;

        // Booked against Brand A's packed stock directly — nobody had to visit Packing first.
        Assert.Equal(new[] { ((int?)h.BrandA, StockBucket.Packed, 30m) }, h.AccessoryAllocation(lineId));

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            Array.Empty<DispatchLineInput>(), new[] { new DispatchAccessoryLineInput(lineId, 30) }));

        Assert.Equal((0m, 70m, 0m), h.AccessoryBrandRow(h.Acc1, h.BrandA));
    }

    [Fact]
    public void Reconcile_rebuilds_a_branded_receipts_contribution_correctly()
    {
        using var h = new TestHarness();
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 50, null));
        h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 60, null, h.BrandA));

        var before = new[]
        {
            h.AccessoryBrandRow(h.Acc1, null),
            h.AccessoryBrandRow(h.Acc1, h.BrandA),
        };

        foreach (var b in h.Db.AccessoryStockBalances) { b.RawOnHand = -999; b.PackedOnHand = -999; b.Reserved = -999; }
        h.Db.SaveChanges();
        h.Stock.ReconcileAll();

        Assert.Equal(before[0], h.AccessoryBrandRow(h.Acc1, null));
        Assert.Equal(before[1], h.AccessoryBrandRow(h.Acc1, h.BrandA));
    }
}
