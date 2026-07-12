using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain.Models;

namespace SaniStock.Domain.Services;

/// <summary>Posts finished-ware production and reverses mistaken postings.</summary>
public class ProductionService
{
    private readonly SaniStockDbContext _db;
    private readonly StockService _stock;
    private readonly IUserContext _user;

    public ProductionService(SaniStockDbContext db, StockService stock, IUserContext user)
    {
        _db = db;
        _stock = stock;
        _user = user;
    }

    /// <summary>
    /// Posts a production entry, increasing finished OnHand for the item+grade+colour.
    /// Creates the stock combination if it does not yet exist.
    /// </summary>
    public ProductionEntry Post(ProductionInput input)
    {
        if (input.Quantity <= 0)
            throw new DomainException("Production quantity must be greater than zero.");

        var entry = new ProductionEntry
        {
            Date = input.Date,
            ItemId = input.ItemId,
            GradeId = input.GradeId,
            ColourId = input.ColourId,
            Quantity = input.Quantity,
            Remarks = input.Remarks,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now
        };
        _db.ProductionEntries.Add(entry);
        _db.SaveChanges(); // assign entry.Id so the movement can reference it

        _stock.ApplyFinished(input.ItemId, input.GradeId, input.ColourId,
            StockMovementType.Production, deltaOnHand: input.Quantity, deltaReserved: 0,
            input.Date, "ProductionEntry", entry.Id, input.Remarks);
        _db.SaveChanges();

        return entry;
    }

    /// <summary>
    /// Reverses a posted production entry by decreasing OnHand by the same quantity and
    /// recording a linked reversing entry. The original row is left untouched (immutable).
    /// </summary>
    public ProductionEntry Reverse(int productionEntryId, string? remarks = null)
    {
        var original = _db.ProductionEntries.Find(productionEntryId)
            ?? throw new DomainException("Production entry not found.");
        if (original.IsReversal)
            throw new DomainException("A reversal entry cannot itself be reversed.");
        if (_db.ProductionEntries.Any(e => e.ReversesEntryId == productionEntryId))
            throw new DomainException("This production entry has already been reversed.");

        var reversal = new ProductionEntry
        {
            Date = DateTime.Today,
            ItemId = original.ItemId,
            GradeId = original.GradeId,
            ColourId = original.ColourId,
            Quantity = original.Quantity,
            Remarks = remarks ?? $"Reversal of production #{original.Id}",
            IsReversal = true,
            ReversesEntryId = original.Id,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now
        };
        _db.ProductionEntries.Add(reversal);
        _db.SaveChanges();

        _stock.ApplyFinished(original.ItemId, original.GradeId, original.ColourId,
            StockMovementType.Adjustment, deltaOnHand: -original.Quantity, deltaReserved: 0,
            reversal.Date, "ProductionEntry", reversal.Id, reversal.Remarks);
        _db.SaveChanges();

        return reversal;
    }
}
