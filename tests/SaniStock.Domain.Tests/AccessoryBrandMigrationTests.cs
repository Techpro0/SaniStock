using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Services;
using Xunit;

namespace SaniStock.Domain.Tests;

/// <summary>
/// Exercises the <c>AddAccessoryBranding</c> migration against a database that already holds
/// accessory stock and an open order — the upgrade case, which is the only way this migration will
/// ever run in the field. Mirrors <see cref="BrandMigrationTests"/>: migrate to the release
/// <em>before</em> this one, insert data through raw SQL exactly as the old app would have left it,
/// then migrate the rest of the way and check what happened to it.
/// </summary>
public class AccessoryBrandMigrationTests : IDisposable
{
    /// <summary>The last migration before accessory branding — the schema a shipped database is on.</summary>
    private const string BeforeAccessoryBranding = "AddDispatchReversal";

    private readonly SqliteConnection _connection;
    private readonly SaniStockDbContext _db;

    public AccessoryBrandMigrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new SaniStockDbContext(
            new DbContextOptionsBuilder<SaniStockDbContext>().UseSqlite(_connection).Options);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private void MigrateTo(string name) => _db.GetService<IMigrator>().Migrate(name);

    private void Exec(string sql) => _db.Database.ExecuteSqlRaw(sql);

    /// <summary>
    /// Recreates a pre-accessory-branding database: one accessory with 100 on hand and 30 reserved,
    /// a ledger that adds up to it, and an open order holding that reservation across two accessory
    /// lines — one auto-bundled to a branded item line (which should inherit its brand), one
    /// standalone (which predates brand entirely).
    /// </summary>
    private void SeedLegacyData()
    {
        Exec("INSERT INTO Items (Code, Name, UnitOfMeasure, IsActive) VALUES ('WB-100', 'Wash Basin 100', 'PCS', 1);");
        Exec("INSERT INTO Grades (Name, SortOrder, IsActive) VALUES ('1st', 1, 1);");
        Exec("INSERT INTO Colours (Name, IsActive) VALUES ('White', 1);");
        Exec("INSERT INTO Parties (Name, IsActive) VALUES ('Acme Traders', 1);");
        Exec("INSERT INTO Accessories (Code, Name, UnitOfMeasure, IsActive) VALUES ('AC-1', 'Pillar Cock', 'PCS', 1);");
        // A second brand alongside the Unbranded one the AddBrand migration already seeded, so
        // there is a real brand for the item line below to have been placed under.
        Exec("INSERT INTO Brands (Code, Name, IsActive) VALUES ('BR-A', 'Brand A', 1);");

        // Cached accessory balance, pre-packing-stage shape: just OnHand/Reserved.
        Exec("INSERT INTO AccessoryStockBalances (AccessoryId, OnHand, Reserved) VALUES (1, 100, 30);");
        Exec(@"INSERT INTO AccessoryStockMovements
                 (Date, AccessoryId, Type, DeltaOnHand, DeltaReserved, SourceType, SourceId, CreatedBy, CreatedAt)
               VALUES
                 ('2026-07-01', 1, 0, 100,  0, 'AccessoryReceipt', 1, 'legacy', '2026-07-01'),
                 ('2026-07-03', 1, 1,   0, 30, 'Order',            1, 'legacy', '2026-07-03');");

        // An open order: one item line under Brand A, plus two accessory lines — one auto-bundled
        // to the item line, one manually added standalone.
        Exec(@"INSERT INTO Orders (OrderNo, PartyId, OrderDate, Status, CreatedBy, CreatedAt)
               VALUES ('ORD-2026-0001', 1, '2026-07-03', 0, 'legacy', '2026-07-03');");
        Exec(@"INSERT INTO OrderLines (OrderId, ItemId, GradeId, ColourId, BrandId, QuantityOrdered, QuantityDispatched, QuantityReserved)
               VALUES (1, 1, 1, 1, 2, 50, 0, 50);");
        Exec(@"INSERT INTO OrderAccessoryLines (OrderId, AccessoryId, SourceOrderLineId, QuantityOrdered, QuantityDispatched, QuantityReserved)
               VALUES (1, 1, 1, 20, 0, 20);");
        Exec(@"INSERT INTO OrderAccessoryLines (OrderId, AccessoryId, SourceOrderLineId, QuantityOrdered, QuantityDispatched, QuantityReserved)
               VALUES (1, 1, NULL, 10, 0, 10);");
    }

    /// <summary>Migrates to the pre-accessory-branding schema, seeds legacy data, then applies it.</summary>
    private void UpgradeLegacyDatabase()
    {
        MigrateTo(BeforeAccessoryBranding);
        SeedLegacyData();
        _db.Database.Migrate();
        _db.ChangeTracker.Clear();
    }

    // ---- The upgrade path -----------------------------------------------------

    [Fact]
    public void Existing_accessory_balance_moves_entirely_into_the_unpacked_bucket()
    {
        UpgradeLegacyDatabase();

        // Accessories never had a packing stage before, so there is nothing to split — everything
        // that was OnHand lands in RawOnHand, on the one (still brand-less) row.
        var bal = _db.AccessoryStockBalances.AsNoTracking().Single();
        Assert.Null(bal.BrandId);
        Assert.Equal(100, bal.RawOnHand);
        Assert.Equal(0, bal.PackedOnHand);
        Assert.Equal(30, bal.Reserved);
    }

    [Fact]
    public void Existing_accessory_movements_carry_their_delta_into_DeltaRawOnHand()
    {
        UpgradeLegacyDatabase();

        var movements = _db.AccessoryStockMovements.AsNoTracking().ToList();
        Assert.All(movements, m => Assert.Null(m.BrandId));
        Assert.Equal(100, movements.Sum(m => m.DeltaRawOnHand));
        Assert.Equal(0, movements.Sum(m => m.DeltaPackedOnHand));
        Assert.Equal(30, movements.Sum(m => m.DeltaReserved));
    }

    [Fact]
    public void Auto_attached_accessory_line_is_backfilled_with_its_parent_item_lines_brand()
    {
        UpgradeLegacyDatabase();
        var brandA = _db.Brands.AsNoTracking().Single(b => b.Code == "BR-A").Id;

        var line = _db.OrderAccessoryLines.AsNoTracking().Single(l => l.SourceOrderLineId != null);
        Assert.Equal(brandA, line.BrandId);
    }

    [Fact]
    public void Standalone_accessory_line_is_backfilled_to_Unbranded()
    {
        UpgradeLegacyDatabase();
        var unbranded = _db.Brands.AsNoTracking().Single(b => b.Code == Brand.DefaultCode).Id;

        var line = _db.OrderAccessoryLines.AsNoTracking().Single(l => l.SourceOrderLineId == null);
        Assert.Equal(unbranded, line.BrandId);
    }

    [Fact]
    public void Open_accessory_lines_get_the_brandless_allocation_they_would_have_booked_with()
    {
        UpgradeLegacyDatabase();

        var allocations = _db.OrderAccessoryLineAllocations.AsNoTracking().ToList();
        Assert.Equal(2, allocations.Count);
        Assert.All(allocations, a =>
        {
            Assert.Null(a.BrandId);
            Assert.Equal(StockBucket.Raw, a.Bucket);
            Assert.Equal(0, a.Priority);
        });
        Assert.Equal(new[] { 20m, 10m }, allocations.Select(a => a.Quantity).OrderByDescending(q => q));
    }

    [Fact]
    public void The_ledger_reproduces_the_migrated_balance_via_reconcile()
    {
        UpgradeLegacyDatabase();

        var before = Snapshot();

        var user = new UserContext { Username = "tester", Role = UserRole.Admin, IsAuthenticated = true };
        new StockService(_db, user).ReconcileAll();
        _db.ChangeTracker.Clear();

        Assert.Equal(before, Snapshot());
    }

    /// <summary>
    /// The upgraded data has to keep working, not just look right: both accessory lines should still
    /// be dispatchable, drawing from the shared pool since nothing has been packed yet.
    /// </summary>
    [Fact]
    public void A_migrated_open_order_can_still_dispatch_its_accessory_lines()
    {
        UpgradeLegacyDatabase();

        var user = new UserContext { Username = "tester", Role = UserRole.Admin, IsAuthenticated = true };
        var stock = new StockService(_db, user);
        var dispatch = new DispatchService(_db, stock, new StockAllocationService(_db, stock),
            new AccessoryStockAllocationService(_db, stock), new NumberSequenceService(_db), user);

        var lines = _db.OrderAccessoryLines.AsNoTracking().OrderBy(l => l.Id).ToList();
        dispatch.Dispatch(new Models.DispatchInput(1, DateTime.Today, null,
            Array.Empty<Models.DispatchLineInput>(),
            new[]
            {
                new Models.DispatchAccessoryLineInput(lines[0].Id, 20),
                new Models.DispatchAccessoryLineInput(lines[1].Id, 10),
            }));
        _db.ChangeTracker.Clear();

        var bal = _db.AccessoryStockBalances.AsNoTracking().Single();
        Assert.Equal(70, bal.RawOnHand);
        Assert.Equal(0, bal.Reserved);
    }

    private Dictionary<(int, int?), (decimal Raw, decimal Packed, decimal Reserved)> Snapshot() =>
        _db.AccessoryStockBalances.AsNoTracking().ToList()
            .ToDictionary(b => (b.AccessoryId, b.BrandId),
                          b => (b.RawOnHand, b.PackedOnHand, b.Reserved));
}
