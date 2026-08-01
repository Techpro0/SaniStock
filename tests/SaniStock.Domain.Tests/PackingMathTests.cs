using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;
using Xunit;

namespace SaniStock.Domain.Tests;

/// <summary>
/// Covers the packing stage: production lands in unpacked stock, packing moves quantity from
/// unpacked to packed without changing the total, and both buckets rebuild from the ledger.
/// </summary>
public class PackingMathTests
{
    // ---- Production lands in unpacked stock only ------------------------------

    [Fact]
    public void Production_lands_in_unpacked_stock_only()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 50, null));

        Assert.Equal((50m, 0m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
        Assert.Equal(50, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
    }

    // ---- Packing moves quantity between buckets -------------------------------

    [Fact]
    public void Packing_moves_unpacked_to_packed_and_leaves_the_total_alone()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));

        h.Pack(h.ItemA, h.Grade1, h.White, 30);

        Assert.Equal((70m, 30m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
        var bal = h.FinishedBalance(h.ItemA, h.Grade1, h.White);
        Assert.Equal(100, bal.OnHand);   // unchanged
        Assert.Equal(0, bal.Reserved);
        Assert.Equal(100, bal.Available);
    }

    /// <summary>The worked example from the feature request, end to end.</summary>
    [Fact]
    public void Worked_example_production_adds_to_unpacked_then_packing_shifts_the_split()
    {
        using var h = new TestHarness();
        // Starting point: 100 in stock, split 50 unpacked / 50 packed.
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 50);
        Assert.Equal((50m, 50m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));

        // Post production of 500: it all lands unpacked.
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 500, null));
        Assert.Equal((550m, 50m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
        Assert.Equal(600, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);

        // Pack 300: the split moves, the total does not.
        h.Pack(h.ItemA, h.Grade1, h.White, 300);
        Assert.Equal((250m, 350m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
        Assert.Equal(600, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
    }

    [Fact]
    public void Packing_more_than_is_unpacked_is_blocked()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 40, null));

        Assert.Throws<DomainException>(() =>
            h.Pack(h.ItemA, h.Grade1, h.White, 41));

        // Nothing moved.
        Assert.Equal((40m, 0m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
    }

    [Fact]
    public void Packing_with_no_stock_at_all_is_blocked()
    {
        using var h = new TestHarness();
        Assert.Throws<DomainException>(() =>
            h.Pack(h.ItemA, h.Grade1, h.White, 1));
    }

    [Fact]
    public void Packing_nonpositive_quantity_is_rejected()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 40, null));
        Assert.Throws<DomainException>(() =>
            h.Pack(h.ItemA, h.Grade1, h.White, 0));
        Assert.Throws<DomainException>(() =>
            h.Pack(h.ItemA, h.Grade1, h.White, -5));
    }

    [Fact]
    public void Packing_cannot_exceed_unpacked_stock_of_a_different_grade()
    {
        using var h = new TestHarness();
        // Stock exists, but as 2nd grade — packing 1st grade must not borrow it.
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade2, h.White, 100, null));

        Assert.Throws<DomainException>(() =>
            h.Pack(h.ItemA, h.Grade1, h.White, 10));
        Assert.Equal((100m, 0m), h.FinishedBuckets(h.ItemA, h.Grade2, h.White));
    }

    // ---- Reversal -------------------------------------------------------------

    [Fact]
    public void Packing_reversal_restores_the_previous_split_and_leaves_the_original()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 20);
        var entry = h.Pack(h.ItemA, h.Grade1, h.White, 30);
        Assert.Equal((50m, 50m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));

        h.Packing.Reverse(entry.Id);

        Assert.Equal((80m, 20m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White)); // back to the earlier split
        Assert.Equal(100, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
        Assert.Equal(30, h.Db.PackingEntries.Find(entry.Id)!.Quantity);          // original untouched
        Assert.Equal(3, h.Db.PackingEntries.Count());                            // two posts + one reversal
    }

    [Fact]
    public void Packing_reversal_twice_is_blocked()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var entry = h.Pack(h.ItemA, h.Grade1, h.White, 30);
        h.Packing.Reverse(entry.Id);

        Assert.Throws<DomainException>(() => h.Packing.Reverse(entry.Id));
    }

    [Fact]
    public void A_packing_reversal_cannot_itself_be_reversed()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var entry = h.Pack(h.ItemA, h.Grade1, h.White, 30);
        var reversal = h.Packing.Reverse(entry.Id);

        Assert.Throws<DomainException>(() => h.Packing.Reverse(reversal.Id));
    }

    [Fact]
    public void Packing_reversal_is_blocked_once_stock_has_shipped_since()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var entry = h.Pack(h.ItemA, h.Grade1, h.White, 40);

        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA,10));
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 10) }, Array.Empty<DispatchAccessoryLineInput>()));

        Assert.Throws<DomainException>(() => h.Packing.Reverse(entry.Id));

        // The blocked reversal changed nothing: 10 shipped out of the packed 40.
        Assert.Equal((60m, 30m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
    }

    [Fact]
    public void Production_reversal_is_blocked_once_the_ware_has_been_packed()
    {
        using var h = new TestHarness();
        var entry = h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 50, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 20);

        // Only 30 is still unpacked, so the 50 cannot be unwound.
        Assert.Throws<DomainException>(() => h.Production.Reverse(entry.Id));
        Assert.Equal((30m, 20m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));

        // Undo the packing first and the production reversal goes through.
        var packing = h.Db.PackingEntries.Single(e => !e.IsReversal);
        h.Packing.Reverse(packing.Id);
        h.Production.Reverse(entry.Id);
        Assert.Equal((0m, 0m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
    }

    // ---- Dispatch draws packed stock first -----------------------------------

    [Fact]
    public void Dispatch_takes_packed_stock_before_unpacked()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 30);
        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA,50));
        var lineId = order.Lines.Single().Id;

        // 20 fits inside the packed 30, so nothing unpacked is touched.
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lineId, 20) }, Array.Empty<DispatchAccessoryLineInput>()));
        Assert.Equal((70m, 10m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));

        // The next 30 exhausts the packed 10 and falls back to unpacked for the other 20.
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lineId, 30) }, Array.Empty<DispatchAccessoryLineInput>()));
        Assert.Equal((50m, 0m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
        Assert.Equal(50, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
    }

    [Fact]
    public void Dispatch_is_still_blocked_when_packed_and_unpacked_together_fall_short()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 20, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 5);
        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA,30));
        var lineId = order.Lines.Single().Id;

        Assert.Throws<DomainException>(() => h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lineId, 25) }, Array.Empty<DispatchAccessoryLineInput>())));

        Assert.Equal((15m, 5m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
    }

    // ---- Reconciliation ------------------------------------------------------

    [Fact]
    public void Reconcile_rebuilds_both_buckets_independently()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 60);
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade2, h.Blue, 40, null));
        h.Pack(h.ItemA, h.Grade2, h.Blue, 10);
        var order = h.Orders.Book(SingleLineOrder(h.PartyX, h.ItemA, h.Grade1, h.White, h.BrandA, 30));
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 25) }, Array.Empty<DispatchAccessoryLineInput>()));

        var before = Snapshot(h);
        // 100 produced, 60 packed as Brand A, 30 booked against that packed stock, 25 shipped.
        // The unpacked pool is untouched and carries no reservation; Brand A holds both.
        Assert.Equal((40m, 0m, 0m), before[(h.ItemA, h.Grade1, h.White, null)]);
        Assert.Equal((0m, 35m, 5m), before[(h.ItemA, h.Grade1, h.White, h.BrandA)]);
        Assert.Equal((75m, 5m, 70m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));

        foreach (var b in h.Db.StockBalances) { b.RawOnHand = -999; b.PackedOnHand = -999; b.Reserved = -999; }
        h.Db.SaveChanges();

        h.Stock.ReconcileAll();

        Assert.Equal(before, Snapshot(h));
    }

    /// <summary>
    /// A packing line is two movements, not one: a movement belongs to a single balance row, and
    /// the unpacked pool is brand-less while the packed stock belongs to a brand. Between them the
    /// pair still nets to zero on the total, which is what makes packing invisible to stock levels.
    /// </summary>
    [Fact]
    public void A_packing_posts_a_brandless_raw_leg_and_a_branded_packed_leg_that_net_to_zero()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 40, h.BrandA);

        var legs = h.Db.StockMovements.Where(m => m.Type == StockMovementType.Packing).ToList();
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

    // ---- Helpers -------------------------------------------------------------

    private static OrderInput SingleLineOrder(int partyId, int itemId, int gradeId, int colourId,
        int brandId, decimal qty) =>
        new(partyId, DateTime.Today, null,
            new[] { new OrderLineInput(itemId, gradeId, colourId, brandId, qty) },
            Array.Empty<OrderAccessoryLineInput>());

    /// <summary>Keyed per balance row — brand included, since that is what reconcile rebuilds.</summary>
    private static Dictionary<(int, int, int, int?), (decimal Raw, decimal Packed, decimal Reserved)> Snapshot(TestHarness h) =>
        h.Db.StockBalances.AsQueryable().ToList()
            .ToDictionary(b => (b.ItemId, b.GradeId, b.ColourId, b.BrandId),
                          b => (b.RawOnHand, b.PackedOnHand, b.Reserved));
}
