using Microsoft.EntityFrameworkCore;
using SaniStock.Data;
using SaniStock.Domain;
using SaniStock.Domain.Services;

namespace SaniStock.App.Infrastructure;

/// <summary>
/// A short-lived unit of work: one fresh <see cref="SaniStockDbContext"/> plus all domain
/// services wired to it. Create one per screen refresh or user action and dispose it, so
/// data is always current and the change-tracker never goes stale.
/// </summary>
public sealed class DomainScope : IDisposable
{
    public SaniStockDbContext Db { get; }
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
    public AuthService Auth { get; }

    public DomainScope(IDbContextFactory<SaniStockDbContext> factory, IUserContext user)
    {
        Db = factory.CreateDbContext();
        Stock = new StockService(Db, user);
        Numbers = new NumberSequenceService(Db);
        Production = new ProductionService(Db, Stock, user);
        AccessoryReceipts = new AccessoryReceiptService(Db, Stock, user);
        Orders = new OrderService(Db, Stock, Numbers, user);
        Dispatch = new DispatchService(Db, Stock, Numbers, user);
        Reports = new ReportService(Db);
        Green = new GreenPieceService(Db, Stock, user);
        Raw = new RawMaterialService(Db, Stock, user);
        Master = new MasterDataService(Db);
        Auth = new AuthService(Db, user);
    }

    public void Dispose() => Db.Dispose();
}

/// <summary>Factory abstraction so view models can spin up a <see cref="DomainScope"/> on demand.</summary>
public interface IDomainScopeFactory
{
    DomainScope Create();
}

public sealed class DomainScopeFactory : IDomainScopeFactory
{
    private readonly IDbContextFactory<SaniStockDbContext> _factory;
    private readonly IUserContext _user;

    public DomainScopeFactory(IDbContextFactory<SaniStockDbContext> factory, IUserContext user)
    {
        _factory = factory;
        _user = user;
    }

    public DomainScope Create() => new(_factory, _user);
}
