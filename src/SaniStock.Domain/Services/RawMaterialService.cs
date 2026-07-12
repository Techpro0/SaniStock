using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain.Models;

namespace SaniStock.Domain.Services;

/// <summary>Records raw-material in/out and maintains its balance.</summary>
public class RawMaterialService
{
    private readonly SaniStockDbContext _db;
    private readonly StockService _stock;
    private readonly IUserContext _user;

    public RawMaterialService(SaniStockDbContext db, StockService stock, IUserContext user)
    {
        _db = db;
        _stock = stock;
        _user = user;
    }

    public RawMaterialEntry Post(RawMaterialInput input)
    {
        if (input.Quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        var entry = new RawMaterialEntry
        {
            Date = input.Date,
            RawMaterialId = input.RawMaterialId,
            IsIssue = input.IsIssue,
            Quantity = input.Quantity,
            Remarks = input.Remarks,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now
        };
        _db.RawMaterialEntries.Add(entry);

        var bal = GetOrCreateBalance(input.RawMaterialId);
        var before = bal.OnHand;
        bal.OnHand += input.IsIssue ? -input.Quantity : input.Quantity;

        _stock.AddAudit(input.IsIssue ? "RawIssue" : "RawReceipt", "RawMaterialBalance", null,
            $"RawMaterial {input.RawMaterialId}: OnHand {before}->{bal.OnHand}", before, bal.OnHand);
        _db.SaveChanges();
        return entry;
    }

    private RawMaterialBalance GetOrCreateBalance(int rawMaterialId)
    {
        var local = _db.RawMaterialBalances.Local.FirstOrDefault(x => x.RawMaterialId == rawMaterialId);
        if (local != null) return local;
        var existing = _db.RawMaterialBalances.FirstOrDefault(x => x.RawMaterialId == rawMaterialId);
        if (existing != null) return existing;
        var created = new RawMaterialBalance { RawMaterialId = rawMaterialId };
        _db.RawMaterialBalances.Add(created);
        return created;
    }
}
