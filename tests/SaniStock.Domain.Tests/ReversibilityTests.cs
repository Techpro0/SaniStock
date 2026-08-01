using Microsoft.EntityFrameworkCore;
using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;
using Xunit;

namespace SaniStock.Domain.Tests;

/// <summary>
/// Covers the principle that nothing in this application is ever physically deleted: every
/// posting can be undone by a linked reversing entry that leaves the original intact, and every
/// master-data record can be switched off and back on.
/// <para>
/// The reversal shapes here mirror <c>ProductionService.Reverse</c>, which
/// <see cref="PackingMathTests"/> already covers for finished ware. What is new is that the
/// accessory, green-ware and raw-material ledgers now behave the same way — including refusing a
/// reversal that would drive a balance negative, which is the failure mode that made these paths
/// unsafe to expose before.
/// </para>
/// </summary>
public class ReversibilityTests
{
    // ---- Accessory receipts ---------------------------------------------------

    [Fact]
    public void Reversing_an_accessory_receipt_takes_the_stock_back_out_and_keeps_the_original()
    {
        using var h = new TestHarness();
        var receipt = h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));

        h.AccessoryReceipts.Reverse(receipt.Id);

        Assert.Equal((0m, 0m, 0m), h.AccessoryBalance(h.Acc1));
        Assert.Equal(100, h.Db.AccessoryReceipts.Find(receipt.Id)!.Quantity);  // original untouched
        Assert.Equal(2, h.Db.AccessoryReceipts.Count());                       // receipt + its reversal
    }

    /// <summary>
    /// The guard that was missing before this path had a button. Receive 100, ship 80, and the
    /// receipt can no longer be unwound — doing so would report −80 accessories in stock.
    /// </summary>
    [Fact]
    public void Reversing_an_accessory_receipt_is_blocked_once_the_stock_has_gone_out()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m) });
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var receipt = h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 100, null));

        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            new[] { new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandA, 80) },
            Array.Empty<OrderAccessoryLineInput>()));
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 80) },
            new[] { new DispatchAccessoryLineInput(order.AccessoryLines.Single().Id, 80) }));

        Assert.Throws<DomainException>(() => h.AccessoryReceipts.Reverse(receipt.Id));

        // The blocked reversal changed nothing — 20 left, none of it negative.
        Assert.Equal(20, h.AccessoryBalance(h.Acc1).OnHand);
    }

    [Fact]
    public void An_accessory_receipt_cannot_be_reversed_twice_or_have_its_reversal_reversed()
    {
        using var h = new TestHarness();
        var receipt = h.AccessoryReceipts.Post(new AccessoryReceiptInput(DateTime.Today, h.Acc1, 50, null));
        var reversal = h.AccessoryReceipts.Reverse(receipt.Id);

        Assert.Throws<DomainException>(() => h.AccessoryReceipts.Reverse(receipt.Id));
        Assert.Throws<DomainException>(() => h.AccessoryReceipts.Reverse(reversal.Id));
    }

    // ---- Green ware -----------------------------------------------------------

    [Fact]
    public void Reversing_a_green_receipt_removes_it_again()
    {
        using var h = new TestHarness();
        var entry = h.Green.Post(new GreenPieceInput(DateTime.Today, h.ItemA, h.White, IsIssue: false, 60, null));

        var reversal = h.Green.Reverse(entry.Id);

        Assert.Equal(0, GreenOnHand(h));
        // The reversal is the mirror of the original, which is what keeps the entries summing
        // to the balance.
        Assert.True(reversal.IsIssue);
        Assert.Equal(entry.Id, reversal.ReversesEntryId);
        Assert.Equal(60, h.Db.GreenPieceEntries.Find(entry.Id)!.Quantity);   // original untouched
    }

    [Fact]
    public void Reversing_a_green_issue_puts_the_pieces_back()
    {
        using var h = new TestHarness();
        h.Green.Post(new GreenPieceInput(DateTime.Today, h.ItemA, h.White, IsIssue: false, 60, null));
        var issue = h.Green.Post(new GreenPieceInput(DateTime.Today, h.ItemA, h.White, IsIssue: true, 25, null));
        Assert.Equal(35, GreenOnHand(h));

        var reversal = h.Green.Reverse(issue.Id);

        Assert.Equal(60, GreenOnHand(h));
        Assert.False(reversal.IsIssue);
    }

    [Fact]
    public void Reversing_a_green_receipt_is_blocked_once_the_pieces_have_been_issued()
    {
        using var h = new TestHarness();
        var receipt = h.Green.Post(new GreenPieceInput(DateTime.Today, h.ItemA, h.White, IsIssue: false, 60, null));
        h.Green.Post(new GreenPieceInput(DateTime.Today, h.ItemA, h.White, IsIssue: true, 50, null));

        // Only 10 left, so un-receiving 60 would go negative.
        Assert.Throws<DomainException>(() => h.Green.Reverse(receipt.Id));
        Assert.Equal(10, GreenOnHand(h));
    }

    [Fact]
    public void Issuing_more_green_ware_than_exists_is_rejected()
    {
        using var h = new TestHarness();
        h.Green.Post(new GreenPieceInput(DateTime.Today, h.ItemA, h.White, IsIssue: false, 20, null));

        Assert.Throws<DomainException>(() =>
            h.Green.Post(new GreenPieceInput(DateTime.Today, h.ItemA, h.White, IsIssue: true, 21, null)));

        Assert.Equal(20, GreenOnHand(h));
        Assert.Single(h.Db.GreenPieceEntries);   // the rejected issue was never written
    }

    [Fact]
    public void A_green_entry_cannot_be_reversed_twice_or_have_its_reversal_reversed()
    {
        using var h = new TestHarness();
        var entry = h.Green.Post(new GreenPieceInput(DateTime.Today, h.ItemA, h.White, IsIssue: false, 30, null));
        var reversal = h.Green.Reverse(entry.Id);

        Assert.Throws<DomainException>(() => h.Green.Reverse(entry.Id));
        Assert.Throws<DomainException>(() => h.Green.Reverse(reversal.Id));
    }

    // ---- Raw material ---------------------------------------------------------

    [Fact]
    public void Reversing_a_raw_receipt_removes_it_again()
    {
        using var h = new TestHarness();
        var entry = h.Raw.Post(new RawMaterialInput(DateTime.Today, h.RawClay, IsIssue: false, 500, null));

        var reversal = h.Raw.Reverse(entry.Id);

        Assert.Equal(0, RawOnHand(h));
        Assert.True(reversal.IsIssue);
        Assert.Equal(entry.Id, reversal.ReversesEntryId);
        Assert.Equal(500, h.Db.RawMaterialEntries.Find(entry.Id)!.Quantity);
    }

    [Fact]
    public void Reversing_a_raw_issue_puts_the_material_back()
    {
        using var h = new TestHarness();
        h.Raw.Post(new RawMaterialInput(DateTime.Today, h.RawClay, IsIssue: false, 500, null));
        var issue = h.Raw.Post(new RawMaterialInput(DateTime.Today, h.RawClay, IsIssue: true, 120, null));
        Assert.Equal(380, RawOnHand(h));

        h.Raw.Reverse(issue.Id);

        Assert.Equal(500, RawOnHand(h));
    }

    [Fact]
    public void Reversing_a_raw_receipt_is_blocked_once_the_material_has_been_consumed()
    {
        using var h = new TestHarness();
        var receipt = h.Raw.Post(new RawMaterialInput(DateTime.Today, h.RawClay, IsIssue: false, 500, null));
        h.Raw.Post(new RawMaterialInput(DateTime.Today, h.RawClay, IsIssue: true, 450, null));

        Assert.Throws<DomainException>(() => h.Raw.Reverse(receipt.Id));
        Assert.Equal(50, RawOnHand(h));
    }

    [Fact]
    public void Issuing_more_raw_material_than_exists_is_rejected()
    {
        using var h = new TestHarness();
        h.Raw.Post(new RawMaterialInput(DateTime.Today, h.RawClay, IsIssue: false, 100, null));

        Assert.Throws<DomainException>(() =>
            h.Raw.Post(new RawMaterialInput(DateTime.Today, h.RawClay, IsIssue: true, 101, null)));

        Assert.Equal(100, RawOnHand(h));
        Assert.Single(h.Db.RawMaterialEntries);
    }

    // ---- Reconciliation now reaches green and raw -----------------------------

    /// <summary>
    /// Because a correction is a mirrored entry rather than an edit, green and raw entries sum to
    /// their balances — so <c>ReconcileAll</c> can rebuild them, which it previously could not.
    /// </summary>
    [Fact]
    public void Reconcile_rebuilds_green_and_raw_balances_including_reversals()
    {
        using var h = new TestHarness();
        h.Green.Post(new GreenPieceInput(DateTime.Today, h.ItemA, h.White, IsIssue: false, 60, null));
        var greenIssue = h.Green.Post(new GreenPieceInput(DateTime.Today, h.ItemA, h.White, IsIssue: true, 25, null));
        h.Green.Reverse(greenIssue.Id);
        h.Raw.Post(new RawMaterialInput(DateTime.Today, h.RawClay, IsIssue: false, 500, null));
        h.Raw.Post(new RawMaterialInput(DateTime.Today, h.RawClay, IsIssue: true, 120, null));

        Assert.Equal(60, GreenOnHand(h));
        Assert.Equal(380, RawOnHand(h));

        foreach (var b in h.Db.GreenPieceBalances) b.OnHand = -999;
        foreach (var b in h.Db.RawMaterialBalances) b.OnHand = -999;
        h.Db.SaveChanges();

        h.Stock.ReconcileAll();

        Assert.Equal(60, GreenOnHand(h));
        Assert.Equal(380, RawOnHand(h));
    }

    // ---- Orders: delete is cancel ---------------------------------------------

    /// <summary>
    /// Deleting an order keeps the order. Its status becomes Cancelled, its lines and allocations
    /// stay on record, and only the reservation is handed back.
    /// </summary>
    [Fact]
    public void Deleting_an_order_keeps_the_record_and_frees_only_the_reservation()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            new[] { new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandA, 30) },
            Array.Empty<OrderAccessoryLineInput>()));

        h.Orders.Cancel(order.Id, "Deleted from the orders list");

        Assert.Equal(OrderStatus.Cancelled, h.Db.Orders.AsNoTracking().Single(o => o.Id == order.Id).Status);
        Assert.Single(h.Db.OrderLines.AsNoTracking().Where(l => l.OrderId == order.Id));
        Assert.NotEmpty(h.Db.OrderLineAllocations.AsNoTracking()
            .Where(a => a.OrderLineId == order.Lines.Single().Id));

        // Stock is intact and fully free again.
        Assert.Equal((100m, 0m, 100m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
    }

    [Fact]
    public void An_order_cannot_be_deleted_twice()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            new[] { new OrderLineInput(h.ItemA, h.Grade1, h.White, h.BrandA, 30) },
            Array.Empty<OrderAccessoryLineInput>()));
        h.Orders.Cancel(order.Id);

        Assert.Throws<DomainException>(() => h.Orders.Cancel(order.Id));

        // The second attempt released nothing a second time.
        Assert.Equal((100m, 0m, 100m), h.FinishedBalance(h.ItemA, h.Grade1, h.White));
    }

    // ---- Master data: delete is deactivate ------------------------------------

    [Fact]
    public void Deleting_master_data_only_switches_it_off_and_it_can_be_restored()
    {
        using var h = new TestHarness();

        SetColourActive(h, h.White, false);
        Assert.False(ColourIsActive(h, h.White));
        Assert.NotNull(h.Db.Colours.Find(h.White));   // the row itself is still there

        SetColourActive(h, h.White, true);
        Assert.True(ColourIsActive(h, h.White));
    }

    [Fact]
    public void Deleting_a_colour_leaves_the_stock_recorded_against_it_intact()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));

        SetColourActive(h, h.White, false);

        Assert.Equal(100, h.FinishedBalance(h.ItemA, h.Grade1, h.White).OnHand);
        Assert.Single(h.Db.ProductionEntries);
    }

    /// <summary>
    /// Grades are the one list that cannot be emptied: every stock row and order line names one,
    /// and the cross-grade borrowing chain is derived from whichever are still active.
    /// </summary>
    [Fact]
    public void The_last_active_grade_cannot_be_deleted()
    {
        using var h = new TestHarness();
        SetGradeActive(h, h.Grade2, false);
        SetGradeActive(h, h.Grade3, false);

        Assert.Throws<DomainException>(() => SetGradeActive(h, h.Grade1, false));

        Assert.True(GradeIsActive(h, h.Grade1));
    }

    [Fact]
    public void A_grade_can_be_deleted_while_others_remain_active()
    {
        using var h = new TestHarness();
        SetGradeActive(h, h.Grade3, false);

        Assert.False(GradeIsActive(h, h.Grade3));
        Assert.Equal(2, h.Db.Grades.AsNoTracking().Count(g => g.IsActive));
    }

    /// <summary>
    /// Dropping an accessory from an item's recipe used to hard-delete the row — the last physical
    /// delete left in the app. It is now deactivated, and adding the accessory back revives the
    /// same row rather than inserting a second one, which the unique (Item, Accessory) pair would
    /// reject anyway.
    /// </summary>
    [Fact]
    public void Dropping_an_accessory_from_a_recipe_deactivates_its_row_instead_of_removing_it()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m), (h.Acc2, 2m) });
        var originalIds = h.Db.ItemAccessoryDefaults.AsNoTracking()
            .Where(d => d.ItemId == h.ItemA).Select(d => d.Id).OrderBy(x => x).ToList();

        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc2, 5m) });

        // The recipe reads as just Acc2...
        Assert.Equal(new[] { h.Acc2 }, h.Master.GetItemAccessoryDefaults(h.ItemA).Select(d => d.AccessoryId));
        // ...but both rows are still there, Acc1's simply switched off.
        var rows = h.Db.ItemAccessoryDefaults.AsNoTracking().Where(d => d.ItemId == h.ItemA).ToList();
        Assert.Equal(2, rows.Count);
        Assert.False(rows.Single(r => r.AccessoryId == h.Acc1).IsActive);

        // Putting Acc1 back reuses its row rather than creating a rival one.
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 3m), (h.Acc2, 5m) });
        var after = h.Db.ItemAccessoryDefaults.AsNoTracking().Where(d => d.ItemId == h.ItemA).ToList();
        Assert.Equal(originalIds, after.Select(d => d.Id).OrderBy(x => x).ToList());
        Assert.Equal(3m, after.Single(r => r.AccessoryId == h.Acc1).QtyPerUnit);
    }

    [Fact]
    public void Clearing_a_recipe_keeps_every_row_and_only_switches_them_off()
    {
        using var h = new TestHarness();
        h.Master.SetItemAccessoryDefaults(h.ItemA, new[] { (h.Acc1, 1m), (h.Acc2, 2m) });

        h.Master.SetItemAccessoryDefaults(h.ItemA, Array.Empty<(int, decimal)>());

        Assert.Empty(h.Master.GetItemAccessoryDefaults(h.ItemA));
        var rows = h.Db.ItemAccessoryDefaults.AsNoTracking().Where(d => d.ItemId == h.ItemA).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.False(r.IsActive));
    }

    // ---- Helpers --------------------------------------------------------------

    // Saves a detached copy rather than mutating the tracked entity, mirroring what MasterList
    // actually does. Mutating the tracked instance would leave the flag flipped in memory even
    // when the save is refused, so a test doing that would pass whether the guard fired or not.
    //
    // The tracker is cleared first because this harness keeps one context alive for the whole
    // test, still tracking the entities it seeded — the app opens a fresh DomainScope per action,
    // so nothing is pre-tracked there and Update() has nothing to collide with.

    private static void SetGradeActive(TestHarness h, int gradeId, bool isActive)
    {
        var g = h.Db.Grades.AsNoTracking().Single(x => x.Id == gradeId);
        h.Db.ChangeTracker.Clear();
        h.Master.SaveGrade(new Grade { Id = g.Id, Name = g.Name, SortOrder = g.SortOrder, IsActive = isActive });
    }

    private static void SetColourActive(TestHarness h, int colourId, bool isActive)
    {
        var c = h.Db.Colours.AsNoTracking().Single(x => x.Id == colourId);
        h.Db.ChangeTracker.Clear();
        h.Master.SaveColour(new Colour { Id = c.Id, Name = c.Name, HexCode = c.HexCode, IsActive = isActive });
    }

    private static bool GradeIsActive(TestHarness h, int gradeId) =>
        h.Db.Grades.AsNoTracking().Single(g => g.Id == gradeId).IsActive;

    private static bool ColourIsActive(TestHarness h, int colourId) =>
        h.Db.Colours.AsNoTracking().Single(c => c.Id == colourId).IsActive;

    private static decimal GreenOnHand(TestHarness h) =>
        h.Db.GreenPieceBalances.Where(b => b.ItemId == h.ItemA && b.ColourId == h.White)
            .Select(b => (decimal?)b.OnHand).FirstOrDefault() ?? 0m;

    private static decimal RawOnHand(TestHarness h) =>
        h.Db.RawMaterialBalances.Where(b => b.RawMaterialId == h.RawClay)
            .Select(b => (decimal?)b.OnHand).FirstOrDefault() ?? 0m;
}
