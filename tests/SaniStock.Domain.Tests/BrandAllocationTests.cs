using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;
using SaniStock.Domain.Services;
using Xunit;

namespace SaniStock.Domain.Tests;

/// <summary>
/// Covers the brand dimension, which exists from packing onward: production is brand-less, packing
/// assigns brand and may split one action across several, packed stock belongs to exactly one
/// brand, and orders are placed against a brand.
/// <para>
/// The rule under test throughout is that allocation narrows the <em>packed</em> steps by brand but
/// leaves the <em>unpacked</em> steps shared — ware in the pool has not been assigned a brand yet,
/// so any order may draw on it — giving the chain
/// packed(brand) 1st → unpacked 1st → packed(brand) 2nd → unpacked 2nd → shortfall.
/// </para>
/// Mirrors <see cref="GradePriorityAllocationTests"/>, which covers the same walk without brand.
/// </summary>
public class BrandAllocationTests
{
    // ---- Packing assigns brand -----------------------------------------------

    [Fact]
    public void Production_stays_brandless_and_packing_is_what_assigns_a_brand()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));

        // Everything sits on the shared pool; no brand row exists yet.
        Assert.Equal((100m, 0m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, null));
        Assert.Equal(0, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandA));

        h.Pack(h.ItemA, h.Grade1, h.White, 40, h.BrandA);

        Assert.Equal((60m, 0m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, null));
        Assert.Equal((0m, 40m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA));
        Assert.Equal((60m, 40m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
        Assert.Equal(100, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand); // total unchanged
    }

    /// <summary>The worked example from the feature request: one run, three brands, one action.</summary>
    [Fact]
    public void One_packing_action_splits_across_several_brands()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 500, null));

        var entries = h.PackSplit(h.ItemA, h.Grade1, h.White,
            (h.BrandA, 200), (h.BrandB, 200), (h.BrandC, 100));

        Assert.Equal(3, entries.Count);
        Assert.Equal(200, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandA));
        Assert.Equal(200, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandB));
        Assert.Equal(100, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandC));
        Assert.Equal((0m, 500m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
        Assert.Equal(500, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);

        // All three rows share one batch, so the screen can group them as a single posting.
        Assert.Single(entries.Select(e => e.BatchId).Distinct());
        Assert.Equal(entries[0].Id, entries[0].BatchId);
    }

    [Fact]
    public void The_brand_lines_are_checked_against_the_unpacked_balance_as_a_group()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 500, null));

        // Each line fits on its own; together they do not.
        Assert.Throws<DomainException>(() => h.PackSplit(h.ItemA, h.Grade1, h.White,
            (h.BrandA, 300), (h.BrandB, 300)));

        // Nothing was posted — not even the line that would have fitted.
        Assert.Equal((500m, 0m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
        Assert.Empty(h.Db.PackingEntries);
    }

    [Fact]
    public void The_same_brand_cannot_appear_twice_in_one_packing()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 500, null));

        Assert.Throws<DomainException>(() => h.PackSplit(h.ItemA, h.Grade1, h.White,
            (h.BrandA, 100), (h.BrandA, 100)));
        Assert.Empty(h.Db.PackingEntries);
    }

    [Fact]
    public void Packing_under_an_unknown_brand_is_rejected()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));

        Assert.Throws<DomainException>(() => h.Pack(h.ItemA, h.Grade1, h.White, 10, brandId: 9999));
    }

    // ---- Per-brand reversal ---------------------------------------------------

    [Fact]
    public void Undoing_one_brands_portion_leaves_the_other_brands_alone()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 500, null));
        var entries = h.PackSplit(h.ItemA, h.Grade1, h.White,
            (h.BrandA, 200), (h.BrandB, 200), (h.BrandC, 100));

        h.Packing.Reverse(entries[1].Id);   // undo Brand B only

        Assert.Equal(200, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandA));
        Assert.Equal(0, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandB));
        Assert.Equal(100, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandC));
        // Brand B's 200 went back to the shared pool; the total never moved.
        Assert.Equal((200m, 300m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
        Assert.Equal(500, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
    }

    [Fact]
    public void Reversal_is_blocked_only_for_the_brand_that_has_shipped_since()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 400, null));
        var entries = h.PackSplit(h.ItemA, h.Grade1, h.White, (h.BrandA, 200), (h.BrandB, 200));

        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 50));
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 50) }, Array.Empty<DispatchAccessoryLineInput>()));

        // Brand A's packed stock is what went out, so its packing cannot be unwound...
        Assert.Throws<DomainException>(() => h.Packing.Reverse(entries[0].Id));
        // ...but Brand B shipped nothing and is still free to undo.
        h.Packing.Reverse(entries[1].Id);

        Assert.Equal(150, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandA));
        Assert.Equal(0, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandB));
    }

    // ---- Booking: packed steps are brand-scoped, unpacked steps are not -------

    [Fact]
    public void Packed_stock_of_the_lines_own_brand_is_drawn_first()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 50, h.BrandA);

        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 30));

        Assert.Equal(new[] { (h.Grade1, (int?)h.BrandA, StockBucket.Packed, 30m) },
            h.BrandAllocation(order.Lines.Single().Id));
        // The reservation lands on Brand A's row, not the shared pool.
        Assert.Equal(30, h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA).Reserved);
        Assert.Equal(0, h.BrandRow(h.ItemA, h.Grade1, h.White, null).Reserved);
    }

    /// <summary>The central brand rule: another brand's boxes can never cover this order.</summary>
    [Fact]
    public void Another_brands_packed_stock_is_never_drawn()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 100, h.BrandB);   // all of it packed as Brand B

        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 40));

        // Nothing is available to Brand A, so the whole line is a shortfall.
        Assert.Equal(new[] { (h.Grade1, (int?)null, StockBucket.Shortfall, 40m) },
            h.BrandAllocation(order.Lines.Single().Id));
        Assert.Equal(0, h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandB).Reserved);
    }

    [Fact]
    public void Unpacked_stock_is_shared_and_covers_an_order_for_any_brand()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 60, h.BrandB);   // 40 unpacked, 60 packed as Brand B

        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 40));

        // Brand A has no packed stock, but the unpacked pool belongs to nobody yet.
        Assert.Equal(new[] { (h.Grade1, (int?)null, StockBucket.Raw, 40m) },
            h.BrandAllocation(order.Lines.Single().Id));
        Assert.Equal(40, h.BrandRow(h.ItemA, h.Grade1, h.White, null).Reserved);
    }

    [Fact]
    public void The_full_chain_runs_packed_brand_then_shared_unpacked_then_the_grade_below()
    {
        using var h = new TestHarness();
        // 1st grade: 40 packed for Brand A, 20 packed for Brand B (invisible here), 40 unpacked.
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.PackSplit(h.ItemA, h.Grade1, h.White, (h.BrandA, 40), (h.BrandB, 20));
        // 2nd grade: 20 packed for Brand A, 30 unpacked.
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade2, h.White, 50, null));
        h.Pack(h.ItemA, h.Grade2, h.White, 20, h.BrandA);

        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 150));

        Assert.Equal(new[]
        {
            (h.Grade1, (int?)h.BrandA, StockBucket.Packed, 40m),  // Brand A's packed 1st
            (h.Grade1, (int?)null,     StockBucket.Raw,    40m),  // shared unpacked 1st
            (h.Grade2, (int?)h.BrandA, StockBucket.Packed, 20m),  // Brand A's packed 2nd
            (h.Grade2, (int?)null,     StockBucket.Raw,    30m),  // shared unpacked 2nd
            (h.Grade1, (int?)null,     StockBucket.Shortfall, 20m),
        }, h.BrandAllocation(order.Lines.Single().Id));

        // Brand B's 20 packed 1st-grade pieces were never touched.
        Assert.Equal(0, h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandB).Reserved);
        Assert.Equal(20, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandB));
    }

    [Fact]
    public void Two_lines_for_different_brands_do_not_compete_for_each_others_packed_stock()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.PackSplit(h.ItemA, h.Grade1, h.White, (h.BrandA, 50), (h.BrandB, 50));

        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            new[]
            {
                new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandA, 50),
                new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandB, 50),
            },
            Array.Empty<OrderAccessoryLineInput>()));

        var lines = order.Lines.ToList();
        Assert.Equal(new[] { (h.Grade1, (int?)h.BrandA, StockBucket.Packed, 50m) }, h.BrandAllocation(lines[0].Id));
        Assert.Equal(new[] { (h.Grade1, (int?)h.BrandB, StockBucket.Packed, 50m) }, h.BrandAllocation(lines[1].Id));
        Assert.Equal(100, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
        Assert.Equal(0, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Available);
    }

    /// <summary>
    /// Two lines for the <em>same</em> brand still see each other's claims, because each line's
    /// reservation is applied before the next is planned.
    /// </summary>
    [Fact]
    public void Two_lines_for_the_same_brand_see_each_others_claims()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 60, h.BrandA);

        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            new[]
            {
                new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandA, 50),
                new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandA, 50),
            },
            Array.Empty<OrderAccessoryLineInput>()));

        var lines = order.Lines.ToList();
        Assert.Equal(new[] { (h.Grade1, (int?)h.BrandA, StockBucket.Packed, 50m) }, h.BrandAllocation(lines[0].Id));
        Assert.Equal(new[]
        {
            (h.Grade1, (int?)h.BrandA, StockBucket.Packed, 10m),  // only 10 packed left
            (h.Grade1, (int?)null,     StockBucket.Raw,    40m),
        }, h.BrandAllocation(lines[1].Id));
    }

    /// <summary>
    /// Booking's counterpart to the dispatch rationing below: two brands compete for one shared
    /// pool, so the second line must see what the first already claimed rather than both counting
    /// the same pieces as free. Lines are allocated one at a time, applying each reservation before
    /// planning the next, which is what makes this work.
    /// </summary>
    [Fact]
    public void Two_brands_booking_against_the_same_pool_see_each_others_claims()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 40, null));

        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            new[]
            {
                new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandA, 30),
                new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandB, 30),
            },
            Array.Empty<OrderAccessoryLineInput>()));
        var lines = order.Lines.ToList();

        Assert.Equal(new[] { (h.Grade1, (int?)null, StockBucket.Raw, 30m) }, h.BrandAllocation(lines[0].Id));
        Assert.Equal(new[]
        {
            (h.Grade1, (int?)null, StockBucket.Raw, 10m),          // only 10 of the pool was left
            (h.Grade1, (int?)null, StockBucket.Shortfall, 20m),
        }, h.BrandAllocation(lines[1].Id));

        // 60 booked against 40 on hand — the extra 20 is the shortfall signal, not double-counted stock.
        Assert.Equal(60, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
        Assert.Equal(-20, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Available);
    }

    /// <summary>
    /// Why a booking can appear to "skip" this grade's unpacked stock and jump to the grade below.
    /// The unpacked pool is shared across brands, so an earlier order for a <em>different</em>
    /// brand can have it entirely claimed. The Stock screen still shows a healthy "Not Packed"
    /// number — that column is on-hand, not free — so the walk looks wrong when it is not.
    /// </summary>
    [Fact]
    public void A_pool_already_claimed_by_another_brand_is_skipped_for_the_grade_below()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade2, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade2, h.White, 40, h.BrandA);

        // Brand B takes the whole 1st-grade unpacked pool first.
        h.Orders.Book(Line(h, h.Grade1, h.BrandB, 100));

        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 50));

        // Brand A has no packed 1st grade and the pool is spoken for, so it goes to 2nd grade —
        // correctly, though the Stock screen still reads "Not Packed: 100" for 1st grade.
        Assert.Equal(new[]
        {
            (h.Grade2, (int?)h.BrandA, StockBucket.Packed, 40m),
            (h.Grade2, (int?)null,     StockBucket.Raw,    10m),
        }, h.BrandAllocation(order.Lines.Single().Id));

        // 100 on hand, 100 booked: there really was nothing free, whatever the column says.
        var firstGrade = h.FinishedBalance(h.ItemA, h.Grade1, h.White);
        Assert.Equal(100, firstGrade.OnHand);
        Assert.Equal(0, firstGrade.Available);
    }

    // ---- Dispatch: the recorded source is preferred, its sibling is the fallback

    /// <summary>
    /// The ordinary flow: book while the stock is still unpacked, pack it under the order's brand,
    /// then ship. The reservation is recorded against the shared pool but the goods are now in the
    /// brand's packed row, so dispatch has to look there — otherwise every book-then-pack order
    /// would become unshippable.
    /// </summary>
    [Fact]
    public void Stock_booked_while_unpacked_can_still_ship_after_it_is_packed_under_that_brand()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 30));
        Assert.Equal(new[] { (h.Grade1, (int?)null, StockBucket.Raw, 30m) },
            h.BrandAllocation(order.Lines.Single().Id));

        h.Pack(h.ItemA, h.Grade1, h.White, 100, h.BrandA);   // everything becomes Brand A packed

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 30) }, Array.Empty<DispatchAccessoryLineInput>()));

        // Goods left Brand A's packed row; the reservation was released from the pool row it sat on.
        Assert.Equal((0m, 70m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA));
        Assert.Equal((0m, 0m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, null));
        Assert.Equal(70, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
    }

    /// <summary>The mirror case: booked against packed stock whose packing was later undone.</summary>
    [Fact]
    public void Stock_booked_as_packed_can_still_ship_after_the_packing_is_undone()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var packing = h.Pack(h.ItemA, h.Grade1, h.White, 50, h.BrandA);
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 50));
        Assert.Equal(new[] { (h.Grade1, (int?)h.BrandA, StockBucket.Packed, 50m) },
            h.BrandAllocation(order.Lines.Single().Id));

        h.Packing.Reverse(packing.Id);   // the packed 50 goes back to the shared pool

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 50) }, Array.Empty<DispatchAccessoryLineInput>()));

        Assert.Equal((50m, 0m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, null));
        Assert.Equal((0m, 0m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA));
    }

    [Fact]
    public void Dispatch_will_not_reach_into_another_brands_packed_stock_to_cover_a_shortfall()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 60));   // booked against the pool

        // The pool is then packed under the wrong brand entirely.
        h.Pack(h.ItemA, h.Grade1, h.White, 100, h.BrandB);

        var ex = Assert.Throws<DomainException>(() => h.Dispatch.Dispatch(
            new DispatchInput(order.Id, DateTime.Today, null,
                new[] { new DispatchLineInput(order.Lines.Single().Id, 60) },
                Array.Empty<DispatchAccessoryLineInput>())));
        Assert.Contains("Produce more first", ex.Message);

        // The rejected dispatch left Brand B's stock and the reservation untouched.
        Assert.Equal(100, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandB));
        Assert.Equal(60, h.BrandRow(h.ItemA, h.Grade1, h.White, null).Reserved);
    }

    [Fact]
    public void Dispatch_spanning_both_rows_releases_each_reservation_from_the_row_holding_it()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 30, h.BrandA);
        // 30 packed for Brand A + 70 unpacked; a 90 line takes all the packed and 60 of the pool.
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 90));

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 90) }, Array.Empty<DispatchAccessoryLineInput>()));

        Assert.Equal((10m, 0m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, null));
        Assert.Equal((0m, 0m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA));
        Assert.Equal(10, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
    }

    /// <summary>
    /// Two lines for different brands are two separate dispatch keys, but they share one physical
    /// unpacked pool. Each must be rationed against what the other already took, or one dispatch
    /// ships more pieces than exist.
    /// </summary>
    [Fact]
    public void Two_brands_drawing_on_the_same_pool_in_one_dispatch_cannot_together_overdraw_it()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 40, null));

        // Nothing is packed, so both lines book against the shared pool: 30 each against only 40.
        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            new[]
            {
                new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandA, 30),
                new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandB, 30),
            },
            Array.Empty<OrderAccessoryLineInput>()));
        var lines = order.Lines.ToList();

        // Shipping both in one go would take 60 out of a pool holding 40.
        Assert.Throws<DomainException>(() => h.Dispatch.Dispatch(
            new DispatchInput(order.Id, DateTime.Today, null,
                new[] { new DispatchLineInput(lines[0].Id, 30), new DispatchLineInput(lines[1].Id, 30) },
                Array.Empty<DispatchAccessoryLineInput>())));

        // The rejected dispatch moved nothing, and never drove the pool negative.
        Assert.Equal((40m, 0m), h.FinishedBuckets(h.ItemA, h.Grade1, h.White));
        Assert.Equal(40, h.BrandRow(h.ItemA, h.Grade1, h.White, null).Raw);

        // What the pool can actually cover still ships.
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lines[0].Id, 30), new DispatchLineInput(lines[1].Id, 10) },
            Array.Empty<DispatchAccessoryLineInput>()));
        Assert.Equal(0, h.BrandRow(h.ItemA, h.Grade1, h.White, null).Raw);
    }

    /// <summary>
    /// The same sharing, but with packed stock in play: each brand's packed row is its own, so only
    /// what spills past it competes for the pool.
    /// </summary>
    [Fact]
    public void Each_brands_packed_stock_is_spent_before_they_compete_for_the_pool()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.PackSplit(h.ItemA, h.Grade1, h.White, (h.BrandA, 25), (h.BrandB, 25));  // 50 left in the pool

        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            new[]
            {
                new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandA, 40),
                new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandB, 40),
            },
            Array.Empty<OrderAccessoryLineInput>()));
        var lines = order.Lines.ToList();

        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lines[0].Id, 40), new DispatchLineInput(lines[1].Id, 40) },
            Array.Empty<DispatchAccessoryLineInput>()));

        // 25 + 25 packed, plus 15 + 15 from the pool = 80 of the 100 that existed.
        Assert.Equal(0, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandA));
        Assert.Equal(0, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandB));
        Assert.Equal(20, h.BrandRow(h.ItemA, h.Grade1, h.White, null).Raw);
        Assert.Equal(20, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
    }

    // ---- Cancellation ---------------------------------------------------------

    [Fact]
    public void Cancelling_releases_each_source_row_in_reverse_draw_order()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 30, h.BrandA);
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 90));
        Assert.Equal(30, h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA).Reserved);
        Assert.Equal(60, h.BrandRow(h.ItemA, h.Grade1, h.White, null).Reserved);

        h.Orders.Cancel(order.Id, "customer withdrew");

        Assert.Equal((70m, 0m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, null));
        Assert.Equal((0m, 30m, 0m), h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA));
        Assert.Equal(100, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Available);
    }

    [Fact]
    public void Cancelling_one_brands_order_frees_nothing_of_another_brands()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.PackSplit(h.ItemA, h.Grade1, h.White, (h.BrandA, 50), (h.BrandB, 50));
        var a = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 50));
        h.Orders.Book(Line(h, h.Grade1, h.BrandB, 50));

        h.Orders.Cancel(a.Id);

        Assert.Equal(0, h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA).Reserved);
        Assert.Equal(50, h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandB).Reserved);
    }

    // ---- Reconciliation -------------------------------------------------------

    [Fact]
    public void Reconcile_rebuilds_the_per_brand_split_from_the_ledger()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 500, null));
        h.PackSplit(h.ItemA, h.Grade1, h.White, (h.BrandA, 200), (h.BrandB, 150));
        var order = h.Orders.Book(Line(h, h.Grade1, h.BrandA, 250));
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 220) }, Array.Empty<DispatchAccessoryLineInput>()));

        var before = Snapshot(h);
        foreach (var b in h.Db.StockBalances) { b.RawOnHand = -999; b.PackedOnHand = -999; b.Reserved = -999; }
        h.Db.SaveChanges();

        h.Stock.ReconcileAll();

        Assert.Equal(before, Snapshot(h));
        // And the brands really are still separated, not collapsed back together.
        Assert.Equal(150, h.PackedFor(h.ItemA, h.Grade1, h.White, h.BrandB));
    }

    // ---- The invariant that keeps the two rows meaningful ---------------------

    [Fact]
    public void A_brandless_movement_cannot_carry_packed_quantity()
    {
        using var h = new TestHarness();
        Assert.Throws<DomainException>(() => h.Stock.ApplyFinished(
            h.ItemA, h.Grade1, h.White, brandId: null, StockMovementType.Packing,
            deltaRawOnHand: 0, deltaPackedOnHand: 10, deltaReserved: 0,
            DateTime.Today, null, null, null));
    }

    [Fact]
    public void A_branded_movement_cannot_carry_unpacked_quantity()
    {
        using var h = new TestHarness();
        Assert.Throws<DomainException>(() => h.Stock.ApplyFinished(
            h.ItemA, h.Grade1, h.White, h.BrandA, StockMovementType.Production,
            deltaRawOnHand: 10, deltaPackedOnHand: 0, deltaReserved: 0,
            DateTime.Today, null, null, null));
    }

    [Fact]
    public void Both_rows_may_hold_reservations()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Pack(h.ItemA, h.Grade1, h.White, 40, h.BrandA);

        // A line spanning both rows is the normal case, not a violation.
        h.Orders.Book(Line(h, h.Grade1, h.BrandA, 80));

        Assert.Equal(40, h.BrandRow(h.ItemA, h.Grade1, h.White, h.BrandA).Reserved);
        Assert.Equal(40, h.BrandRow(h.ItemA, h.Grade1, h.White, null).Reserved);
    }

    // ---- The physical-draw split, in isolation --------------------------------

    [Theory]
    // Packed covers it on its own — the pool is left intact for whatever brand needs it next.
    [InlineData(50, 100, 60, 50, 0)]
    // Packed runs out part way and the pool covers the rest.
    [InlineData(50, 100, 20, 20, 30)]
    // Nothing packed for this brand (or its packing was undone): all of it out of the pool.
    [InlineData(50, 100, 0, 0, 50)]
    // Nothing left in the pool: all of it out of packed.
    [InlineData(50, 0, 60, 50, 0)]
    // Both rows together fall short: returns only what exists, so the caller can reject it.
    [InlineData(60, 10, 20, 20, 10)]
    public void PlanPhysicalDraw_takes_packed_first_then_falls_back_to_the_shared_pool(
        int quantity, int rawAvailable, int packedAvailable, int expectPacked, int expectRaw)
    {
        var draw = StockAllocationService.PlanPhysicalDraw(quantity, rawAvailable, packedAvailable);

        Assert.Equal(expectPacked, draw.FromPacked);
        Assert.Equal(expectRaw, draw.FromRaw);
    }

    // ---- Helpers -------------------------------------------------------------

    private static OrderInput Line(TestHarness h, int gradeId, int brandId, decimal qty) =>
        new(h.PartyX, DateTime.Today, null,
            new[] { new OrderLineInput(h.ItemA, gradeId, h.White, brandId, qty) },
            Array.Empty<OrderAccessoryLineInput>());

    private static Dictionary<(int, int, int, int?), (decimal Raw, decimal Packed, decimal Reserved)> Snapshot(TestHarness h) =>
        h.Db.StockBalances.AsQueryable().ToList()
            .ToDictionary(b => (b.ItemId, b.GradeId, b.ColourId, b.BrandId),
                          b => (b.RawOnHand, b.PackedOnHand, b.Reserved));
}
