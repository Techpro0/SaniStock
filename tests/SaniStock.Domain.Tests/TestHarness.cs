using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain;
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
    public NumberSequenceService Numbers { get; }
    public ProductionService Production { get; }
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
    public int White { get; }
    public int Blue { get; }
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
        Numbers = new NumberSequenceService(Db);
        Production = new ProductionService(Db, Stock, User);
        AccessoryReceipts = new AccessoryReceiptService(Db, Stock, User);
        Orders = new OrderService(Db, Stock, Numbers, User);
        Dispatch = new DispatchService(Db, Stock, Numbers, User);
        Reports = new ReportService(Db);
        Green = new GreenPieceService(Db, Stock, User);
        Raw = new RawMaterialService(Db, Stock, User);
        Master = new MasterDataService(Db);

        var itemA = Add(new Item { Code = "WB-100", Name = "Wash Basin 100" });
        var itemB = Add(new Item { Code = "WC-200", Name = "Water Closet 200" });
        var g1 = Add(new Grade { Name = "1st", SortOrder = 1 });
        var g2 = Add(new Grade { Name = "2nd", SortOrder = 2 });
        var white = Add(new Colour { Name = "White" });
        var blue = Add(new Colour { Name = "Ivory" });
        var acc = Add(new Accessory { Code = "AC-1", Name = "Pillar Cock" });
        var acc2 = Add(new Accessory { Code = "AC-2", Name = "Seat Cover" });
        var px = Add(new Party { Name = "Acme Traders" });
        var py = Add(new Party { Name = "Best Ceramics" });
        var clay = Add(new RawMaterial { Name = "Ball Clay", UnitOfMeasure = "KG" });
        Db.SaveChanges();

        ItemA = itemA.Id; ItemB = itemB.Id;
        Grade1 = g1.Id; Grade2 = g2.Id;
        White = white.Id; Blue = blue.Id;
        Acc1 = acc.Id; Acc2 = acc2.Id;
        PartyX = px.Id; PartyY = py.Id;
        RawClay = clay.Id;
    }

    private T Add<T>(T entity) where T : class
    {
        Db.Set<T>().Add(entity);
        return entity;
    }

    /// <summary>Reads the current finished balance for a key (0/0 if none exists yet).</summary>
    public (decimal OnHand, decimal Reserved, decimal Available) FinishedBalance(int itemId, int gradeId, int colourId)
    {
        var b = Db.StockBalances.AsNoTracking()
            .FirstOrDefault(x => x.ItemId == itemId && x.GradeId == gradeId && x.ColourId == colourId);
        return b is null ? (0, 0, 0) : (b.OnHand, b.Reserved, b.Available);
    }

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
