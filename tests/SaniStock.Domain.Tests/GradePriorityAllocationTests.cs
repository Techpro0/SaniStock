using Microsoft.EntityFrameworkCore;
using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;
using Xunit;

namespace SaniStock.Domain.Tests;

/// <summary>
/// Covers grade-priority allocation: a 1st-grade order line is covered from packed 1st, then
/// unpacked 1st, then packed 2nd, then unpacked 2nd, and lower grades never borrow at all.
/// Dispatch and cancellation must unwind the same sources the booking drew from.
/// </summary>
public class GradePriorityAllocationTests
{
    // ---- Draw order ----------------------------------------------------------

    [Fact]
    public void Packed_first_grade_is_drawn_first()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 100, packed: 50);   // 50 unpacked / 50 packed

        var order = h.Orders.Book(Line(h, h.Grade1, 30));

        Assert.Equal(new[] { (h.Grade1, StockBucket.Packed, 30m) }, h.Allocation(order.Lines.Single().Id));
        Assert.Equal(30, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
    }

    [Fact]
    public void Falls_through_to_unpacked_first_grade_when_packed_runs_out()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 100, packed: 20);   // 80 unpacked / 20 packed

        var order = h.Orders.Book(Line(h, h.Grade1, 50));

        Assert.Equal(new[]
        {
            (h.Grade1, StockBucket.Packed, 20m),
            (h.Grade1, StockBucket.Raw, 30m),
        }, h.Allocation(order.Lines.Single().Id));
        Assert.Equal(50, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
        Assert.Equal(0, h.FinishedBalance(h.ItemA, h.Grade2, h.White).Reserved);
    }

    [Fact]
    public void Falls_through_to_packed_second_grade_when_first_grade_runs_out()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 10, packed: 10);    // 0 unpacked / 10 packed
        Stock(h, h.Grade2, produced: 50, packed: 40);    // 10 unpacked / 40 packed

        var order = h.Orders.Book(Line(h, h.Grade1, 30));

        Assert.Equal(new[]
        {
            (h.Grade1, StockBucket.Packed, 10m),
            (h.Grade2, StockBucket.Packed, 20m),
        }, h.Allocation(order.Lines.Single().Id));
        // The reservation sits on whichever grade physically holds the goods.
        Assert.Equal(10, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
        Assert.Equal(20, h.FinishedBalance(h.ItemA, h.Grade2, h.White).Reserved);
    }

    [Fact]
    public void All_four_sources_are_drawn_in_priority_order_and_the_rest_is_a_shortfall()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 100, packed: 40);   // 60 unpacked / 40 packed
        Stock(h, h.Grade2, produced: 50, packed: 20);    // 30 unpacked / 20 packed

        var order = h.Orders.Book(Line(h, h.Grade1, 200));

        Assert.Equal(new[]
        {
            (h.Grade1, StockBucket.Packed, 40m),
            (h.Grade1, StockBucket.Raw, 60m),
            (h.Grade2, StockBucket.Packed, 20m),
            (h.Grade2, StockBucket.Raw, 30m),
            (h.Grade1, StockBucket.Shortfall, 50m),   // 200 - 150 available anywhere
        }, h.Allocation(order.Lines.Single().Id));

        // The uncovered 50 lands on the ordered grade, so that is what the shortfall report shows.
        var first = h.FinishedBalance(h.ItemA, h.Grade1, h.White);
        Assert.Equal(100, first.OnHand);
        Assert.Equal(150, first.Reserved);
        Assert.Equal(-50, first.Available);

        // 2nd grade is exactly used up — its stock is not double-counted as free.
        var second = h.FinishedBalance(h.ItemA, h.Grade2, h.White);
        Assert.Equal(50, second.OnHand);
        Assert.Equal(50, second.Reserved);
        Assert.Equal(0, second.Available);
    }

    /// <summary>
    /// Pins the exact source sequence, including the recorded <c>Priority</c> values, against a
    /// setup where all four sources are needed and every one of them is non-empty — so any
    /// reordering (in particular checking the grade below before this grade's unpacked stock)
    /// shows up as a different sequence rather than being masked by an empty source.
    /// <para>
    /// The order matters because <c>Priority</c> is what dispatch consumes ascending and
    /// cancellation releases descending. Getting it wrong would ship the grade below while this
    /// grade's own unpacked stock sat unused.
    /// </para>
    /// </summary>
    [Fact]
    public void First_grade_is_fully_exhausted_before_the_second_grade_is_touched()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 100, packed: 30);   // 70 unpacked / 30 packed
        Stock(h, h.Grade2, produced: 50, packed: 20);    // 30 unpacked / 20 packed

        // Exactly the total of all four sources, so every one is drawn and none is a shortfall.
        var order = h.Orders.Book(Line(h, h.Grade1, 150));
        var lineId = order.Lines.Single().Id;

        var steps = h.Db.OrderLineAllocations.AsNoTracking()
            .Where(a => a.OrderLineId == lineId)
            .OrderBy(a => a.Id)     // insertion order, NOT Priority — so a wrong Priority is visible
            .Select(a => new { a.Priority, a.GradeId, a.BrandId, a.Bucket, a.Quantity })
            .ToList();

        Assert.Equal(new[]
        {
            (0, h.Grade1, (int?)h.BrandA, StockBucket.Packed, 30m),  // 1. packed 1st, this brand
            (1, h.Grade1, (int?)null,     StockBucket.Raw,    70m),  // 2. unpacked 1st, shared
            (2, h.Grade2, (int?)h.BrandA, StockBucket.Packed, 20m),  // 3. packed 2nd, this brand
            (3, h.Grade2, (int?)null,     StockBucket.Raw,    30m),  // 4. unpacked 2nd, shared
        }, steps.Select(s => (s.Priority, s.GradeId, s.BrandId, s.Bucket, s.Quantity)));

        // Stated independently of the literal above: every 1st-grade source comes before every
        // 2nd-grade one, and 1st grade is drained dry (30 + 70 = all 100 that existed) first.
        var firstGradeSteps = steps.Where(s => s.GradeId == h.Grade1).ToList();
        var secondGradeSteps = steps.Where(s => s.GradeId == h.Grade2).ToList();
        Assert.True(firstGradeSteps.Max(s => s.Priority) < secondGradeSteps.Min(s => s.Priority));
        Assert.Equal(100, firstGradeSteps.Sum(s => s.Quantity));
        Assert.Equal(0, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Available);
        Assert.Equal(0, h.FinishedBalance(h.ItemA, h.Grade2, h.White).Available);
    }

    [Fact]
    public void A_source_that_covers_everything_stops_the_walk_early()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 100, packed: 100);
        Stock(h, h.Grade2, produced: 100, packed: 100);

        var order = h.Orders.Book(Line(h, h.Grade1, 100));

        Assert.Equal(new[] { (h.Grade1, StockBucket.Packed, 100m) }, h.Allocation(order.Lines.Single().Id));
        Assert.Equal(0, h.FinishedBalance(h.ItemA, h.Grade2, h.White).Reserved); // never touched
    }

    [Fact]
    public void Booking_with_no_stock_anywhere_is_all_shortfall_on_the_ordered_grade()
    {
        using var h = new TestHarness();
        var order = h.Orders.Book(Line(h, h.Grade1, 40));

        Assert.Equal(new[] { (h.Grade1, StockBucket.Shortfall, 40m) }, h.Allocation(order.Lines.Single().Id));
        Assert.Equal(-40, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Available);
    }

    // ---- Only the top grade borrows, and only downwards ----------------------

    [Fact]
    public void Second_grade_lines_never_borrow_from_any_other_grade()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 100, packed: 100);  // plenty of 1st grade
        Stock(h, h.Grade3, produced: 100, packed: 100);  // plenty of 3rd grade

        var order = h.Orders.Book(Line(h, h.Grade2, 40));

        // Behaves exactly as before the feature: reserves only against its own grade and goes short.
        Assert.Equal(new[] { (h.Grade2, StockBucket.Shortfall, 40m) }, h.Allocation(order.Lines.Single().Id));
        Assert.Equal(-40, h.FinishedBalance(h.ItemA, h.Grade2, h.White).Available);
        Assert.Equal(100, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Available);
        Assert.Equal(100, h.FinishedBalance(h.ItemA, h.Grade3, h.White).Available);
    }

    [Fact]
    public void Third_grade_lines_never_borrow_from_any_other_grade()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 100, packed: 100);
        Stock(h, h.Grade2, produced: 100, packed: 100);

        var order = h.Orders.Book(Line(h, h.Grade3, 25));

        Assert.Equal(new[] { (h.Grade3, StockBucket.Shortfall, 25m) }, h.Allocation(order.Lines.Single().Id));
        Assert.Equal(100, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Available);
        Assert.Equal(100, h.FinishedBalance(h.ItemA, h.Grade2, h.White).Available);
    }

    [Fact]
    public void Borrowing_never_reaches_past_the_second_grade()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade3, produced: 100, packed: 100);  // only 3rd grade has stock

        var order = h.Orders.Book(Line(h, h.Grade1, 30));

        Assert.Equal(new[] { (h.Grade1, StockBucket.Shortfall, 30m) }, h.Allocation(order.Lines.Single().Id));
        Assert.Equal(100, h.FinishedBalance(h.ItemA, h.Grade3, h.White).Available);
    }

    [Fact]
    public void Allocation_is_per_colour()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade2, produced: 100, packed: 100);                    // 2nd grade, White
        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            new[] { new OrderLineInput(h.ItemA, h.Grade1, h.Blue, h.BrandA, 30) },   // ordered in Ivory
            Array.Empty<OrderAccessoryLineInput>()));

        // A different colour is a different product; the White 2nd-grade stock is not borrowable.
        Assert.Equal(new[] { (h.Grade1, StockBucket.Shortfall, 30m) }, h.Allocation(order.Lines.Single().Id));
        Assert.Equal(100, h.FinishedBalance(h.ItemA, h.Grade2, h.White).Available);
    }

    // ---- Competing lines see each other's claims -----------------------------

    [Fact]
    public void A_later_booking_cannot_claim_stock_an_earlier_one_already_borrowed()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade2, produced: 50, packed: 50);    // only 2nd grade has stock

        // A 1st-grade order borrows 40 of the 50.
        var borrowed = h.Orders.Book(Line(h, h.Grade1, 40));
        Assert.Equal(new[] { (h.Grade2, StockBucket.Packed, 40m) }, h.Allocation(borrowed.Lines.Single().Id));

        // A genuine 2nd-grade order now only finds 10 free and goes short for the other 20.
        var direct = h.Orders.Book(Line(h, h.Grade2, 30));
        Assert.Equal(new[]
        {
            (h.Grade2, StockBucket.Packed, 10m),
            (h.Grade2, StockBucket.Shortfall, 20m),
        }, h.Allocation(direct.Lines.Single().Id));

        var second = h.FinishedBalance(h.ItemA, h.Grade2, h.White);
        Assert.Equal(50, second.OnHand);
        Assert.Equal(70, second.Reserved);
        Assert.Equal(-20, second.Available);
    }

    [Fact]
    public void Two_lines_on_one_order_do_not_both_claim_the_same_stock()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 30, packed: 30);

        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            new[]
            {
                new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandA, 20),
                new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandA, 20),
            },
            Array.Empty<OrderAccessoryLineInput>()));

        var lines = order.Lines.ToList();
        Assert.Equal(new[] { (h.Grade1, StockBucket.Packed, 20m) }, h.Allocation(lines[0].Id));
        Assert.Equal(new[]
        {
            (h.Grade1, StockBucket.Packed, 10m),
            (h.Grade1, StockBucket.Shortfall, 10m),
        }, h.Allocation(lines[1].Id));
        Assert.Equal(40, h.FinishedBalance(h.ItemA, h.Grade1, h.White).Reserved);
    }

    // ---- Dispatch consumes the recorded sources ------------------------------

    [Fact]
    public void Dispatch_deducts_the_borrowed_grades_stock_not_the_ordered_grades()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 10, packed: 10);
        Stock(h, h.Grade2, produced: 40, packed: 40);

        var order = h.Orders.Book(Line(h, h.Grade1, 30));   // 10 from 1st, 20 borrowed from 2nd
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 30) }, Array.Empty<DispatchAccessoryLineInput>()));

        // 1st grade fully consumed, and the 20 really came out of 2nd-grade stock.
        Assert.Equal((0m, 0m, 0m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
        var second = h.FinishedBalance(h.ItemA, h.Grade2, h.White);
        Assert.Equal(20, second.OnHand);
        Assert.Equal(0, second.Reserved);
        Assert.Equal(OrderStatus.Dispatched, h.Db.Orders.Find(order.Id)!.Status);
    }

    [Fact]
    public void Partial_dispatch_consumes_the_best_source_first()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 10, packed: 10);
        Stock(h, h.Grade2, produced: 40, packed: 40);
        var order = h.Orders.Book(Line(h, h.Grade1, 30));   // 10 from 1st, 20 from 2nd
        var lineId = order.Lines.Single().Id;

        // The first 10 come entirely out of 1st grade.
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lineId, 10) }, Array.Empty<DispatchAccessoryLineInput>()));
        Assert.Equal((0m, 0m, 0m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
        Assert.Equal(40, h.FinishedBalance(h.ItemA, h.Grade2, h.White).OnHand);
        Assert.Equal(20, h.FinishedBalance(h.ItemA, h.Grade2, h.White).Reserved);

        // Only then does it move on to the borrowed 2nd-grade stock.
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(lineId, 20) }, Array.Empty<DispatchAccessoryLineInput>()));
        Assert.Equal((20m, 0m, 20m), h.FinishedBalance(h.ItemA, h.Grade2, h.White));
    }

    [Fact]
    public void Dispatch_of_a_borrowed_line_is_blocked_when_the_lending_grade_ran_dry()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade2, produced: 30, packed: 30);
        var order = h.Orders.Book(Line(h, h.Grade1, 30));   // all 30 borrowed from 2nd grade

        // The 2nd-grade stock is physically written off before shipping.
        var production = h.Db.ProductionEntries.Single(e => e.GradeId == h.Grade2);
        h.Packing.Reverse(h.Db.PackingEntries.Single(e => !e.IsReversal).Id);
        h.Production.Reverse(production.Id);
        Assert.Equal(0, h.FinishedBalance(h.ItemA, h.Grade2, h.White).OnHand);

        Assert.Throws<DomainException>(() => h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 30) }, Array.Empty<DispatchAccessoryLineInput>())));
    }

    [Fact]
    public void A_shortfall_allocation_ships_once_the_stock_is_finally_produced()
    {
        using var h = new TestHarness();
        var order = h.Orders.Book(Line(h, h.Grade1, 40));   // booked against nothing
        Assert.Equal(new[] { (h.Grade1, StockBucket.Shortfall, 40m) }, h.Allocation(order.Lines.Single().Id));

        // Produce and pack it afterwards, then ship.
        Stock(h, h.Grade1, produced: 40, packed: 40);
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 40) }, Array.Empty<DispatchAccessoryLineInput>()));

        Assert.Equal((0m, 0m, 0m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
        Assert.Equal(OrderStatus.Dispatched, h.Db.Orders.Find(order.Id)!.Status);
    }

    // ---- Cancellation releases the recorded sources --------------------------

    [Fact]
    public void Cancel_releases_every_grade_the_line_borrowed_from()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 100, packed: 40);
        Stock(h, h.Grade2, produced: 50, packed: 20);
        var order = h.Orders.Book(Line(h, h.Grade1, 200));   // draws all four sources + shortfall

        h.Orders.Cancel(order.Id, "customer withdrew");

        // Every borrowed reservation is handed back; stock itself is untouched.
        Assert.Equal((100m, 0m, 100m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
        Assert.Equal((50m, 0m, 50m), h.FinishedBalance(h.ItemA, h.Grade2, h.White));
        Assert.Equal(OrderStatus.Cancelled, h.Db.Orders.Find(order.Id)!.Status);
    }

    [Fact]
    public void Cancel_after_partial_dispatch_releases_only_what_is_still_held()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 10, packed: 10);
        Stock(h, h.Grade2, produced: 40, packed: 40);
        var order = h.Orders.Book(Line(h, h.Grade1, 30));   // 10 from 1st, 20 from 2nd

        // Ship the 1st-grade part, then cancel the rest.
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 10) }, Array.Empty<DispatchAccessoryLineInput>()));
        h.Orders.Cancel(order.Id);

        Assert.Equal((0m, 0m, 0m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
        // The borrowed 20 goes back to 2nd grade, which keeps all its stock.
        Assert.Equal((40m, 0m, 40m), h.FinishedBalance(h.ItemA, h.Grade2, h.White));
    }

    [Fact]
    public void Cancel_unwinds_borrowed_grades_before_the_lines_own_grade()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 10, packed: 10);
        Stock(h, h.Grade2, produced: 40, packed: 40);
        var order = h.Orders.Book(Line(h, h.Grade1, 30));
        var line = h.Db.OrderLines.Find(order.Lines.Single().Id)!;

        h.Orders.Cancel(order.Id);

        // Release order is recorded on the allocations: the borrowed source is given back too.
        var allocations = h.Db.OrderLineAllocations.Where(a => a.OrderLineId == line.Id)
            .OrderBy(a => a.Priority).ToList();
        Assert.All(allocations, a => Assert.Equal(a.Quantity, a.QuantityReleased));
        Assert.All(allocations, a => Assert.Equal(0, a.QuantityReserved));
    }

    // ---- Accessories are unaffected -----------------------------------------

    [Fact]
    public void Accessory_bundling_is_unchanged_by_cross_grade_allocation()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m), (h.Acc2, 2m) });
        Stock(h, h.Grade2, produced: 100, packed: 100);   // the item will be met from 2nd grade

        var order = h.Orders.Book(Line(h, h.Grade1, 10));

        // Item borrowed across grades...
        Assert.Equal(new[] { (h.Grade2, StockBucket.Packed, 10m) }, h.Allocation(order.Lines.Single().Id));
        // ...while accessories still reserve against their own stock, exactly as before.
        Assert.Equal((0, 10, -10), h.AccessoryBalance(h.Acc1));
        Assert.Equal((0, 20, -20), h.AccessoryBalance(h.Acc2));
    }

    // ---- Reports and reconciliation -----------------------------------------

    [Fact]
    public void Shortfall_report_attributes_a_borrowed_grade_to_the_order_that_borrowed_it()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade2, produced: 20, packed: 20);

        // A 1st-grade order takes all 20 of the 2nd-grade stock and then some.
        h.Orders.Book(Line(h, h.Grade1, 50));

        var rows = h.Reports.GetShortfall();

        // 1st grade is short the 30 it could not source anywhere.
        var first = rows.Single(r => r.Grade == "1st");
        Assert.Equal(30, first.Shortfall);
        Assert.Equal(0, first.OnHand);
        var firstDriver = Assert.Single(first.Drivers);
        Assert.Equal("1st", firstDriver.OrderedGrade);
        Assert.Equal(30, firstDriver.PendingQuantity);

        // 2nd grade is not short — its 20 exactly covers what was borrowed.
        Assert.DoesNotContain(rows, r => r.Grade == "2nd");
    }

    [Fact]
    public void Shortfall_report_names_the_higher_grade_order_behind_a_borrowed_grades_shortfall()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade2, produced: 20, packed: 20);
        h.Orders.Book(Line(h, h.Grade1, 20));           // borrows all 20 of the 2nd-grade stock
        h.Orders.Book(Line(h, h.Grade2, 15));           // a real 2nd-grade order now finds nothing

        var second = h.Reports.GetShortfall().Single(r => r.Grade == "2nd");

        Assert.Equal(15, second.Shortfall);              // 35 reserved against 20 on hand
        Assert.Equal(2, second.Drivers.Count);
        // Both orders are named, and the report says which one was actually a 1st-grade order.
        Assert.Contains(second.Drivers, d => d.OrderedGrade == "1st" && d.PendingQuantity == 20);
        Assert.Contains(second.Drivers, d => d.OrderedGrade == "2nd" && d.PendingQuantity == 15);
    }

    [Fact]
    public void Reconcile_rebuilds_cross_grade_reservations_from_the_ledger()
    {
        using var h = new TestHarness();
        Stock(h, h.Grade1, produced: 100, packed: 40);
        Stock(h, h.Grade2, produced: 50, packed: 20);
        var order = h.Orders.Book(Line(h, h.Grade1, 200));
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 120) }, Array.Empty<DispatchAccessoryLineInput>()));

        var before = Snapshot(h);
        foreach (var b in h.Db.StockBalances) { b.RawOnHand = -999; b.PackedOnHand = -999; b.Reserved = -999; }
        h.Db.SaveChanges();

        h.Stock.ReconcileAll();

        Assert.Equal(before, Snapshot(h));
    }

    // ---- Helpers -------------------------------------------------------------

    /// <summary>Produces <paramref name="produced"/> of ItemA/White in a grade and packs part of it.</summary>
    private static void Stock(TestHarness h, int gradeId, decimal produced, decimal packed)
    {
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, gradeId, h.White, produced, null));
        if (packed > 0)
            h.Pack(h.ItemA, gradeId, h.White, packed);
    }

    private static OrderInput Line(TestHarness h, int gradeId, decimal qty) =>
        new(h.PartyX, DateTime.Today, null,
            new[] { new OrderLineInput(h.ItemA, gradeId, h.White, h.BrandA, qty) },
            Array.Empty<OrderAccessoryLineInput>());

    /// <summary>Keyed per balance row, brand included — one combination now spans several rows.</summary>
    private static Dictionary<(int, int, int, int?), (decimal Raw, decimal Packed, decimal Reserved)> Snapshot(TestHarness h) =>
        h.Db.StockBalances.AsQueryable().ToList()
            .ToDictionary(b => (b.ItemId, b.GradeId, b.ColourId, b.BrandId),
                          b => (b.RawOnHand, b.PackedOnHand, b.Reserved));
}
