using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain.Models;

namespace SaniStock.Domain.Services;

/// <summary>Records green (unfired) ware in/out and maintains its balance.</summary>
public class GreenPieceService
{
    private readonly SaniStockDbContext _db;
    private readonly StockService _stock;
    private readonly IUserContext _user;

    public GreenPieceService(SaniStockDbContext db, StockService stock, IUserContext user)
    {
        _db = db;
        _stock = stock;
        _user = user;
    }

    public GreenPieceEntry Post(GreenPieceInput input)
    {
        if (input.Quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        var entry = new GreenPieceEntry
        {
            Date = input.Date,
            ItemId = input.ItemId,
            ColourId = input.ColourId,
            IsIssue = input.IsIssue,
            Quantity = input.Quantity,
            Remarks = input.Remarks,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now
        };
        _db.GreenPieceEntries.Add(entry);

        var bal = GetOrCreateBalance(input.ItemId, input.ColourId);
        var before = bal.OnHand;
        bal.OnHand += input.IsIssue ? -input.Quantity : input.Quantity;

        _stock.AddAudit(input.IsIssue ? "GreenIssue" : "GreenReceipt", "GreenPieceBalance", null,
            $"Item {input.ItemId}/C{input.ColourId}: OnHand {before}->{bal.OnHand}", before, bal.OnHand);
        _db.SaveChanges();
        return entry;
    }

    private GreenPieceBalance GetOrCreateBalance(int itemId, int colourId)
    {
        var local = _db.GreenPieceBalances.Local.FirstOrDefault(x => x.ItemId == itemId && x.ColourId == colourId);
        if (local != null) return local;
        var existing = _db.GreenPieceBalances.FirstOrDefault(x => x.ItemId == itemId && x.ColourId == colourId);
        if (existing != null) return existing;
        var created = new GreenPieceBalance { ItemId = itemId, ColourId = colourId };
        _db.GreenPieceBalances.Add(created);
        return created;
    }
}
