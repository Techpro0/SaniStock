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
/// Exercises the <c>AddBrand</c> migration against a database that already holds packed stock and
/// open orders — the upgrade case, which is the only way this migration will ever run in the field.
/// <para>
/// Every other suite builds its schema with <c>EnsureCreated()</c> from the model, which means the
/// migration chain itself is otherwise never run. That gap matters most for a migration that
/// rewrites existing rows, so these tests take the long way round: migrate to the release
/// <em>before</em> Brand, insert data through raw SQL exactly as the old app would have left it,
/// then migrate the rest of the way and check what happened to it.
/// </para>
/// </summary>
public class BrandMigrationTests : IDisposable
{
    /// <summary>The last migration before Brand — the schema a shipped 2.1.0 database is on.</summary>
    private const string BeforeBrand = "AddPackingAndGradeAllocation";

    private readonly SqliteConnection _connection;
    private readonly SaniStockDbContext _db;

    public BrandMigrationTests()
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
    /// Recreates a pre-Brand database: one item/grade/colour with stock split 40 unpacked / 60
    /// packed, a ledger that adds up to it, and an open order holding 50 of it.
    /// </summary>
    private void SeedLegacyData()
    {
        Exec("INSERT INTO Items (Code, Name, UnitOfMeasure, IsActive) VALUES ('WB-100', 'Wash Basin 100', 'PCS', 1);");
        Exec("INSERT INTO Grades (Name, SortOrder, IsActive) VALUES ('1st', 1, 1);");
        Exec("INSERT INTO Colours (Name, IsActive) VALUES ('White', 1);");
        Exec("INSERT INTO Parties (Name, IsActive) VALUES ('Acme Traders', 1);");

        // Cached balance: 100 produced, 60 of it packed, 50 reserved by the open order below.
        Exec(@"INSERT INTO StockBalances (ItemId, GradeId, ColourId, RawOnHand, PackedOnHand, Reserved)
               VALUES (1, 1, 1, 40, 60, 50);");

        // The ledger it was built from: production, a packing carrying both legs on one row, and
        // the reservation. Note the packing row is exactly the shape Brand has to split in two.
        Exec(@"INSERT INTO StockMovements
                 (Date, ItemId, GradeId, ColourId, Type, DeltaRawOnHand, DeltaPackedOnHand, DeltaReserved,
                  SourceType, SourceId, CreatedBy, CreatedAt)
               VALUES
                 ('2026-07-01', 1, 1, 1, 0, 100,   0,  0, 'ProductionEntry', 1, 'legacy', '2026-07-01'),
                 ('2026-07-02', 1, 1, 1, 6, -60,  60,  0, 'PackingEntry',    1, 'legacy', '2026-07-02'),
                 ('2026-07-03', 1, 1, 1, 1,   0,   0, 50, 'Order',           1, 'legacy', '2026-07-03');");

        Exec(@"INSERT INTO PackingEntries (Date, ItemId, GradeId, ColourId, Quantity, IsReversal, CreatedBy, CreatedAt)
               VALUES ('2026-07-02', 1, 1, 1, 60, 0, 'legacy', '2026-07-02');");

        // An open (Booked) order still holding all 50.
        Exec(@"INSERT INTO Orders (OrderNo, PartyId, OrderDate, Status, CreatedBy, CreatedAt)
               VALUES ('ORD-2026-0001', 1, '2026-07-03', 0, 'legacy', '2026-07-03');");
        Exec(@"INSERT INTO OrderLines (OrderId, ItemId, GradeId, ColourId, QuantityOrdered, QuantityDispatched, QuantityReserved)
               VALUES (1, 1, 1, 1, 50, 0, 50);");
        Exec(@"INSERT INTO OrderLineAllocations
                 (OrderLineId, GradeId, Bucket, Priority, Quantity, QuantityDispatched, QuantityReleased)
               VALUES (1, 1, 1, 0, 50, 0, 0);");
    }

    /// <summary>Migrates to the pre-Brand schema, seeds legacy data, then applies Brand.</summary>
    private void UpgradeLegacyDatabase()
    {
        MigrateTo(BeforeBrand);
        SeedLegacyData();
        _db.Database.Migrate();
        _db.ChangeTracker.Clear();
    }

    // ---- The upgrade path -----------------------------------------------------

    [Fact]
    public void Upgrading_seeds_exactly_one_Unbranded_brand()
    {
        UpgradeLegacyDatabase();

        var brand = Assert.Single(_db.Brands.AsNoTracking().ToList());
        Assert.Equal(Brand.DefaultCode, brand.Code);
        // Left active on purpose: pre-existing packed stock has to stay visible and shippable.
        Assert.True(brand.IsActive);
    }

    [Fact]
    public void Seeding_after_the_upgrade_does_not_add_a_second_brand()
    {
        UpgradeLegacyDatabase();

        // DbSeeder runs at every startup and seeds the same code the migration used, so an
        // upgraded database and a fresh one converge on one brand rather than two similar ones.
        DbSeeder.EnsureSeeded(_db);

        Assert.Single(_db.Brands.AsNoTracking().Where(b => b.Code == Brand.DefaultCode).ToList());
    }

    [Fact]
    public void Existing_packed_stock_moves_to_the_Unbranded_row_and_the_pool_keeps_the_unpacked()
    {
        UpgradeLegacyDatabase();
        var unbranded = _db.Brands.AsNoTracking().Single().Id;

        var pool = _db.StockBalances.AsNoTracking().Single(b => b.BrandId == null);
        Assert.Equal(40, pool.RawOnHand);
        Assert.Equal(0, pool.PackedOnHand);      // the invariant: brand-less rows hold no packed stock

        var branded = _db.StockBalances.AsNoTracking().Single(b => b.BrandId == unbranded);
        Assert.Equal(0, branded.RawOnHand);      // and branded rows hold no unpacked stock
        Assert.Equal(60, branded.PackedOnHand);
    }

    /// <summary>
    /// The deliberate choice: reservations are not split packed-first across the new rows. That
    /// split cannot be expressed in the ledger, so reconcile would undo it and the cache would
    /// disagree with the movements.
    /// </summary>
    [Fact]
    public void Existing_reservations_stay_whole_on_the_brandless_row()
    {
        UpgradeLegacyDatabase();
        var unbranded = _db.Brands.AsNoTracking().Single().Id;

        Assert.Equal(50, _db.StockBalances.AsNoTracking().Single(b => b.BrandId == null).Reserved);
        Assert.Equal(0, _db.StockBalances.AsNoTracking().Single(b => b.BrandId == unbranded).Reserved);
    }

    [Fact]
    public void Open_allocations_stay_brandless_even_though_the_line_gets_a_brand()
    {
        UpgradeLegacyDatabase();
        var unbranded = _db.Brands.AsNoTracking().Single().Id;

        // The line is now Unbranded, because an order must name a brand...
        Assert.Equal(unbranded, _db.OrderLines.AsNoTracking().Single().BrandId);
        // ...but its allocation stays brand-less, matching where the reservation was left.
        Assert.Null(_db.OrderLineAllocations.AsNoTracking().Single().BrandId);
    }

    [Fact]
    public void Existing_packing_entries_are_assigned_to_the_Unbranded_brand()
    {
        UpgradeLegacyDatabase();
        var unbranded = _db.Brands.AsNoTracking().Single().Id;

        var entry = _db.PackingEntries.AsNoTracking().Single();
        Assert.Equal(unbranded, entry.BrandId);
        // A historic packing was a batch of one; null reads as "just me" via `BatchId ?? Id`.
        Assert.Null(entry.BatchId);
    }

    /// <summary>
    /// The migration's real job: leave the ledger in a state that reproduces the new balances.
    /// A packing movement carried both legs on one row, which is no longer legal once brand splits
    /// them, so the packed leg is copied onto the Unbranded key and zeroed on the original.
    /// </summary>
    [Fact]
    public void The_ledger_is_split_so_reconcile_reproduces_the_migrated_balances()
    {
        UpgradeLegacyDatabase();

        var before = Snapshot();

        var user = new UserContext { Username = "tester", Role = UserRole.Admin, IsAuthenticated = true };
        new StockService(_db, user).ReconcileAll();
        _db.ChangeTracker.Clear();

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void No_movement_violates_the_bucket_brand_invariant_after_the_upgrade()
    {
        UpgradeLegacyDatabase();

        var movements = _db.StockMovements.AsNoTracking().ToList();
        Assert.All(movements, m =>
        {
            if (m.BrandId is null) Assert.Equal(0, m.DeltaPackedOnHand);
            else Assert.Equal(0, m.DeltaRawOnHand);
        });
        // The split preserved the totals rather than inventing or losing quantity.
        Assert.Equal(40, movements.Sum(m => m.DeltaRawOnHand));
        Assert.Equal(60, movements.Sum(m => m.DeltaPackedOnHand));
    }

    /// <summary>
    /// The upgraded data has to keep working, not just look right. The migrated order holds its
    /// reservation on the brand-less row while the goods sit on the Unbranded packed row — the
    /// exact case dispatch's sibling fallback exists for.
    /// </summary>
    [Fact]
    public void A_migrated_open_order_can_still_be_dispatched()
    {
        UpgradeLegacyDatabase();
        var unbranded = _db.Brands.AsNoTracking().Single().Id;

        var user = new UserContext { Username = "tester", Role = UserRole.Admin, IsAuthenticated = true };
        var stock = new StockService(_db, user);
        var dispatch = new DispatchService(_db, stock, new StockAllocationService(_db, stock),
            new AccessoryStockAllocationService(_db, stock), new NumberSequenceService(_db), user);

        var lineId = _db.OrderLines.AsNoTracking().Single().Id;
        dispatch.Dispatch(new Models.DispatchInput(1, DateTime.Today, null,
            new[] { new Models.DispatchLineInput(lineId, 50) },
            Array.Empty<Models.DispatchAccessoryLineInput>()));
        _db.ChangeTracker.Clear();

        // 50 shipped out of the 100 that existed, drawn from the packed Unbranded stock first.
        var pool = _db.StockBalances.AsNoTracking().Single(b => b.BrandId == null);
        var branded = _db.StockBalances.AsNoTracking().Single(b => b.BrandId == unbranded);
        Assert.Equal(40, pool.RawOnHand);
        Assert.Equal(0, pool.Reserved);
        Assert.Equal(10, branded.PackedOnHand);
        Assert.Equal(50, pool.RawOnHand + branded.PackedOnHand);
    }

    [Fact]
    public void Shortfall_is_judged_across_the_combination_so_the_split_creates_no_phantom()
    {
        UpgradeLegacyDatabase();

        // The brand-less row alone reads 40 on hand against 50 reserved — negative in isolation.
        Assert.Equal(-10, _db.StockBalances.AsNoTracking().Single(b => b.BrandId == null).Available);

        // Measured across the whole combination it is covered, and nothing is reported.
        var reports = new ReportService(_db);
        var row = Assert.Single(reports.GetFinishedStock().Rows);
        Assert.Equal(100, row.OnHand);
        Assert.Equal(50, row.Reserved);
        Assert.Empty(reports.GetShortfall());
    }

    // ---- The fresh-install path -----------------------------------------------

    [Fact]
    public void A_brand_new_database_migrates_cleanly_and_is_ready_to_pack()
    {
        _db.Database.Migrate();
        DbSeeder.EnsureSeeded(_db);
        _db.ChangeTracker.Clear();

        // No stock, no leftover brand rows — just the one starter brand, so Packing works without
        // a first trip to Setup Lists.
        Assert.Empty(_db.StockBalances.AsNoTracking().ToList());
        var brand = Assert.Single(_db.Brands.AsNoTracking().ToList());
        Assert.Equal(Brand.DefaultCode, brand.Code);
    }

    /// <summary>
    /// Drives the whole brand-aware flow against a schema built by the <em>migrations</em> rather
    /// than by <c>EnsureCreated()</c>. Every other suite builds from the model, so a migration that
    /// had drifted from the entities would pass all of them and fail on a real user's database.
    /// </summary>
    [Fact]
    public void The_migrated_schema_supports_the_full_pack_book_dispatch_flow()
    {
        _db.Database.Migrate();
        DbSeeder.EnsureSeeded(_db);

        Exec("INSERT INTO Items (Code, Name, UnitOfMeasure, IsActive) VALUES ('WB-100', 'Wash Basin 100', 'PCS', 1);");
        Exec("INSERT INTO Colours (Name, IsActive) VALUES ('White', 1);");
        Exec("INSERT INTO Parties (Name, IsActive) VALUES ('Acme Traders', 1);");
        Exec("INSERT INTO Brands (Code, Name, IsActive) VALUES ('BR-A', 'Brand A', 1);");
        _db.ChangeTracker.Clear();

        var itemId = _db.Items.AsNoTracking().Single().Id;
        var gradeId = _db.Grades.AsNoTracking().OrderBy(g => g.SortOrder).First().Id;
        var colourId = _db.Colours.AsNoTracking().Single().Id;
        var partyId = _db.Parties.AsNoTracking().Single().Id;
        var brandId = _db.Brands.AsNoTracking().Single(b => b.Code == "BR-A").Id;

        var user = new UserContext { Username = "tester", Role = UserRole.Admin, IsAuthenticated = true };
        var stock = new StockService(_db, user);
        var allocations = new StockAllocationService(_db, stock);
        var accessoryAllocations = new AccessoryStockAllocationService(_db, stock);
        var numbers = new NumberSequenceService(_db);

        new ProductionService(_db, stock, user)
            .Post(new Models.ProductionInput(DateTime.Today, itemId, gradeId, colourId, 100, null));
        new PackingService(_db, stock, user).Post(new Models.PackingInput(
            DateTime.Today, itemId, gradeId, colourId,
            new[] { new Models.PackingBrandLine(brandId, 60) }, null));

        var order = new OrderService(_db, stock, allocations, accessoryAllocations, numbers, user).Book(
            new Models.OrderInput(partyId, DateTime.Today, null,
                new[] { new Models.OrderLineInput(itemId, gradeId, colourId, brandId, 80) },
                Array.Empty<Models.OrderAccessoryLineInput>()));

        new DispatchService(_db, stock, allocations, accessoryAllocations, numbers, user).Dispatch(
            new Models.DispatchInput(order.Id, DateTime.Today, null,
                new[] { new Models.DispatchLineInput(order.Lines.Single().Id, 80) },
                Array.Empty<Models.DispatchAccessoryLineInput>()));
        _db.ChangeTracker.Clear();

        // 60 packed + 20 unpacked went out, leaving 20 in the pool and nothing reserved.
        Assert.Equal(20, _db.StockBalances.AsNoTracking().Sum(b => b.RawOnHand + b.PackedOnHand));
        Assert.Equal(0, _db.StockBalances.AsNoTracking().Sum(b => b.Reserved));
    }

    private Dictionary<(int, int, int, int?), (decimal Raw, decimal Packed, decimal Reserved)> Snapshot() =>
        _db.StockBalances.AsNoTracking().ToList()
            .ToDictionary(b => (b.ItemId, b.GradeId, b.ColourId, b.BrandId),
                          b => (b.RawOnHand, b.PackedOnHand, b.Reserved));
}
