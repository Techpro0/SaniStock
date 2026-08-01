using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;
using SaniStock.Domain.Services;

namespace SaniStock.Domain.Tests;

/// <summary>
/// Spins up an isolated in-memory SQLite database (real SQL engine, not the EF in-memory
/// provider) with seeded master data and all domain services wired to a single context.
/// Disposing closes the connection and discards the database.
/// </summary>
public sealed class TestHarness : IDisposable
{
    private readonly SqliteConnection _connection;

    public SaniStockDbContext Db { get; }
    public UserContext User { get; }
    public StockService Stock { get; }
    public StockAllocationService Allocations { get; }
    public NumberSequenceService Numbers { get; }
    public ProductionService Production { get; }
    public PackingService Packing { get; }
    public AccessoryReceiptService AccessoryReceipts { get; }
    public OrderService Orders { get; }
    public DispatchService Dispatch { get; }
    public ReportService Reports { get; }
    public GreenPieceService Green { get; }
    public RawMaterialService Raw { get; }
    public MasterDataService Master { get; }

    // Seeded ids for convenience.
    public int ItemA { get; }
    public int ItemB { get; }
    public int Grade1 { get; }
    public int Grade2 { get; }
    public int Grade3 { get; }
    public int White { get; }
    public int Blue { get; }

    /// <summary>
    /// Three brands, because brand only means anything once stock can be split between them.
    /// <see cref="BrandA"/> doubles as the default for tests that predate brand and only care that
    /// packed stock exists somewhere.
    /// </summary>
    public int BrandA { get; }
    public int BrandB { get; }
    public int BrandC { get; }

    public int Acc1 { get; }
    public int Acc2 { get; }
    public int PartyX { get; }
    public int PartyY { get; }
    public int RawClay { get; }

    public TestHarness()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SaniStockDbContext>()
            .UseSqlite(_connection)
            .Options;
        Db = new SaniStockDbContext(options);
        Db.Database.EnsureCreated();

        User = new UserContext { Username = "tester", Role = UserRole.Admin, IsAuthenticated = true };
        Stock = new StockService(Db, User);
        Allocations = new StockAllocationService(Db, Stock);
        Numbers = new NumberSequenceService(Db);
        Production = new ProductionService(Db, Stock, User);
        Packing = new PackingService(Db, Stock, User);
        AccessoryReceipts = new AccessoryReceiptService(Db, Stock, User);
        Orders = new OrderService(Db, Stock, Allocations, Numbers, User);
        Dispatch = new DispatchService(Db, Stock, Allocations, Numbers, User);
        Reports = new ReportService(Db);
        Green = new GreenPieceService(Db, Stock, User);
        Raw = new RawMaterialService(Db, Stock, User);
        Master = new MasterDataService(Db);

        var itemA = Add(new Item { Code = "WB-100", Name = "Wash Basin 100" });
        var itemB = Add(new Item { Code = "WC-200", Name = "Water Closet 200" });
        var g1 = Add(new Grade { Name = "1st", SortOrder = 1 });
        var g2 = Add(new Grade { Name = "2nd", SortOrder = 2 });
        var g3 = Add(new Grade { Name = "3rd", SortOrder = 3 });
        var white = Add(new Colour { Name = "White" });
        var blue = Add(new Colour { Name = "Ivory" });
        var brandA = Add(new Brand { Code = "BR-A", Name = "Brand A" });
        var brandB = Add(new Brand { Code = "BR-B", Name = "Brand B" });
        var brandC = Add(new Brand { Code = "BR-C", Name = "Brand C" });
        var acc = Add(new Accessory { Code = "AC-1", Name = "Pillar Cock" });
        var acc2 = Add(new Accessory { Code = "AC-2", Name = "Seat Cover" });
        var px = Add(new Party { Name = "Acme Traders" });
        var py = Add(new Party { Name = "Best Ceramics" });
        var clay = Add(new RawMaterial { Name = "Ball Clay", UnitOfMeasure = "KG" });
        Db.SaveChanges();

        ItemA = itemA.Id; ItemB = itemB.Id;
        Grade1 = g1.Id; Grade2 = g2.Id; Grade3 = g3.Id;
        White = white.Id; Blue = blue.Id;
        BrandA = brandA.Id; BrandB = brandB.Id; BrandC = brandC.Id;
        Acc1 = acc.Id; Acc2 = acc2.Id;
        PartyX = px.Id; PartyY = py.Id;
        RawClay = clay.Id;
    }

    private T Add<T>(T entity) where T : class
    {
        Db.Set<T>().Add(entity);
        return entity;
    }

    // ---- Convenience wrappers ------------------------------------------------

    /// <summary>
    /// Packs <paramref name="quantity"/> under a single brand, defaulting to <see cref="BrandA"/>.
    /// Most tests only care that stock became packed, not which brand holds it.
    /// </summary>
    public PackingEntry Pack(int itemId, int gradeId, int colourId, decimal quantity, int? brandId = null) =>
        Packing.Post(new PackingInput(DateTime.Today, itemId, gradeId, colourId,
            new[] { new PackingBrandLine(brandId ?? BrandA, quantity) }, null)).Single();

    /// <summary>
    /// Packs one action split across several brands, e.g. <c>PackSplit(item, g, c, (BrandA, 200),
    /// (BrandB, 300))</c>. Returns the posted entries in the order given.
    /// </summary>
    public IReadOnlyList<PackingEntry> PackSplit(int itemId, int gradeId, int colourId,
        params (int BrandId, decimal Quantity)[] lines) =>
        Packing.Post(new PackingInput(DateTime.Today, itemId, gradeId, colourId,
            lines.Select(l => new PackingBrandLine(l.BrandId, l.Quantity)).ToList(), null));

    // ---- Balance assertions --------------------------------------------------

    /// <summary>
    /// Reads the finished balance for a combination, summed across the brand-less unpacked row and
    /// every brand's packed row (0/0/0 if none exists yet). This is the level the user sees and the
    /// level shortfall is judged at — an individual row can read negative on its own while the
    /// combination is fully covered.
    /// </summary>
    public (decimal OnHand, decimal Reserved, decimal Available) FinishedBalance(int itemId, int gradeId, int colourId)
    {
        var rows = Db.StockBalances.AsNoTracking()
            .Where(x => x.ItemId == itemId && x.GradeId == gradeId && x.ColourId == colourId)
            .ToList();
        if (rows.Count == 0) return (0, 0, 0);
        var onHand = rows.Sum(b => b.OnHand);
        var reserved = rows.Sum(b => b.Reserved);
        return (onHand, reserved, onHand - reserved);
    }

    /// <summary>The unpacked/packed split for a combination, packed summed across every brand.</summary>
    public (decimal Raw, decimal Packed) FinishedBuckets(int itemId, int gradeId, int colourId)
    {
        var rows = Db.StockBalances.AsNoTracking()
            .Where(x => x.ItemId == itemId && x.GradeId == gradeId && x.ColourId == colourId)
            .ToList();
        return (rows.Sum(b => b.RawOnHand), rows.Sum(b => b.PackedOnHand));
    }

    /// <summary>
    /// One balance row exactly: <paramref name="brandId"/> null is the shared unpacked pool, a
    /// brand id is that brand's packed stock. Use this to check the split brand actually created.
    /// </summary>
    public (decimal Raw, decimal Packed, decimal Reserved) BrandRow(int itemId, int gradeId, int colourId, int? brandId)
    {
        var b = Db.StockBalances.AsNoTracking()
            .FirstOrDefault(x => x.ItemId == itemId && x.GradeId == gradeId && x.ColourId == colourId
                                 && x.BrandId == brandId);
        return b is null ? (0, 0, 0) : (b.RawOnHand, b.PackedOnHand, b.Reserved);
    }

    /// <summary>Packed quantity held for one brand.</summary>
    public decimal PackedFor(int itemId, int gradeId, int colourId, int brandId) =>
        BrandRow(itemId, gradeId, colourId, brandId).Packed;

    // ---- Allocation assertions -----------------------------------------------

    /// <summary>The recorded allocation sources for an order line, in draw order.</summary>
    public List<(int GradeId, StockBucket Bucket, decimal Quantity)> Allocation(int orderLineId) =>
        Db.OrderLineAllocations.AsNoTracking()
            .Where(a => a.OrderLineId == orderLineId)
            .OrderBy(a => a.Priority)
            .Select(a => new { a.GradeId, a.Bucket, a.Quantity })
            .ToList()
            .Select(a => (a.GradeId, a.Bucket, a.Quantity))
            .ToList();

    /// <summary>
    /// The recorded allocation sources for an order line including the brand each drew from, in
    /// draw order. Brand is null for the shared unpacked pool and for a shortfall.
    /// </summary>
    public List<(int GradeId, int? BrandId, StockBucket Bucket, decimal Quantity)> BrandAllocation(int orderLineId) =>
        Db.OrderLineAllocations.AsNoTracking()
            .Where(a => a.OrderLineId == orderLineId)
            .OrderBy(a => a.Priority)
            .Select(a => new { a.GradeId, a.BrandId, a.Bucket, a.Quantity })
            .ToList()
            .Select(a => (a.GradeId, a.BrandId, a.Bucket, a.Quantity))
            .ToList();

    public (decimal OnHand, decimal Reserved, decimal Available) AccessoryBalance(int accessoryId)
    {
        var b = Db.AccessoryStockBalances.AsNoTracking().FirstOrDefault(x => x.AccessoryId == accessoryId);
        return b is null ? (0, 0, 0) : (b.OnHand, b.Reserved, b.Available);
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
