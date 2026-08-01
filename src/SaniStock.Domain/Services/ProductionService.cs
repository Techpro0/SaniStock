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
    /// Posts a production entry, increasing <em>unpacked</em> finished stock for the item+grade+colour.
    /// Newly made ware is never packed until a packing entry says so. Creates the stock combination
    /// if it does not yet exist.
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

        // Brand-less by definition: ware leaves the kiln as one undifferentiated pool and is only
        // assigned a brand when someone packs it.
        _stock.ApplyFinished(input.ItemId, input.GradeId, input.ColourId, brandId: null,
            StockMovementType.Production, deltaRawOnHand: input.Quantity, deltaPackedOnHand: 0,
            deltaReserved: 0, input.Date, "ProductionEntry", entry.Id, input.Remarks);
        _db.SaveChanges();

        return entry;
    }

    /// <summary>
    /// Reverses a posted production entry by removing the same quantity from unpacked stock and
    /// recording a linked reversing entry. The original row is left untouched (immutable).
    /// Blocked once the ware has been packed — undo the packing first, so the correction unwinds
    /// in the same order it was applied and no bucket is driven negative.
    /// </summary>
    public ProductionEntry Reverse(int productionEntryId, string? remarks = null)
    {
        var original = _db.ProductionEntries.Find(productionEntryId)
            ?? throw new DomainException("Production entry not found.");
        if (original.IsReversal)
            throw new DomainException("A reversal entry cannot itself be reversed.");
        if (_db.ProductionEntries.Any(e => e.ReversesEntryId == productionEntryId))
            throw new DomainException("This production entry has already been reversed.");

        var rawOnHand = _stock.FindFinishedBalance(original.ItemId, original.GradeId, original.ColourId)
            ?.RawOnHand ?? 0m;
        if (rawOnHand < original.Quantity)
            throw new DomainException(
                $"This production of {original.Quantity:0.###} cannot be undone: only {rawOnHand:0.###} " +
                "is still unpacked. Undo the packing for this item first.");

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

        _stock.ApplyFinished(original.ItemId, original.GradeId, original.ColourId, brandId: null,
            StockMovementType.Adjustment, deltaRawOnHand: -original.Quantity, deltaPackedOnHand: 0,
            deltaReserved: 0, reversal.Date, "ProductionEntry", reversal.Id, reversal.Remarks);
        _db.SaveChanges();

        return reversal;
    }
}
