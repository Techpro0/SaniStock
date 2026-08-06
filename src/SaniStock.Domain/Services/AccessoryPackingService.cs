using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain.Models;

namespace SaniStock.Domain.Services;

/// <summary>
/// Packs received accessory stock: moves quantity out of the shared unpacked pool for an accessory
/// and into one or more brands' packed stock. Mirrors <see cref="PackingService"/> for finished ware,
/// minus grade/colour — accessories have neither. Total on-hand never changes; only the split and
/// which brand owns it.
/// </summary>
public class AccessoryPackingService
{
    private readonly SaniStockDbContext _db;
    private readonly StockService _stock;
    private readonly IUserContext _user;

    public AccessoryPackingService(SaniStockDbContext db, StockService stock, IUserContext user)
    {
        _db = db;
        _stock = stock;
        _user = user;
    }

    /// <summary>
    /// Posts one accessory packing action, moving quantity from unpacked into the packed stock of
    /// each brand named in <see cref="AccessoryPackingInput.Lines"/>. The lines' quantities are
    /// checked <em>together</em> against the unpacked balance, exactly as <see cref="PackingService.Post"/>
    /// does. Returns the posted entries, one per brand, in input order.
    /// </summary>
    public IReadOnlyList<AccessoryPackingEntry> Post(AccessoryPackingInput input)
    {
        if (input.Lines.Count == 0)
            throw new DomainException("Add at least one brand to pack under.");
        if (input.Lines.Any(l => l.Quantity <= 0))
            throw new DomainException("Every packing quantity must be greater than zero.");
        if (input.Lines.Select(l => l.BrandId).Distinct().Count() != input.Lines.Count)
            throw new DomainException("The same brand is listed twice — combine them into one line.");

        foreach (var brandId in input.Lines.Select(l => l.BrandId))
        {
            if (!_db.Brands.Any(x => x.Id == brandId))
                throw new DomainException("Selected brand does not exist.");
        }

        var total = input.Lines.Sum(l => l.Quantity);
        var rawOnHand = _stock.FindAccessoryBalance(input.AccessoryId)?.RawOnHand ?? 0m;
        if (total > rawOnHand)
            throw new DomainException(
                $"Cannot pack {total:0.###} — only {rawOnHand:0.###} is unpacked for " +
                $"{Describe(input.AccessoryId)}. Add stock first.");

        var entries = new List<AccessoryPackingEntry>();
        foreach (var line in input.Lines)
        {
            var entry = new AccessoryPackingEntry
            {
                Date = input.Date,
                AccessoryId = input.AccessoryId,
                BrandId = line.BrandId,
                Quantity = line.Quantity,
                Remarks = input.Remarks,
                CreatedBy = _user.Username,
                CreatedAt = DateTime.Now
            };
            _db.AccessoryPackingEntries.Add(entry);
            entries.Add(entry);
        }
        _db.SaveChanges(); // assign ids so the movements can reference them and the batch can be stamped

        var batchId = entries[0].Id;
        foreach (var entry in entries)
        {
            entry.BatchId = batchId;
            PostMovements(entry, entry.Quantity, input.Remarks);
        }
        _db.SaveChanges();

        return entries;
    }

    /// <summary>
    /// Reverses one brand's accessory packing entry, moving its quantity back into the shared
    /// unpacked pool. Mirrors <see cref="PackingService.Reverse"/>: blocked once anything has shipped
    /// for that accessory+brand since the packing was posted.
    /// </summary>
    public AccessoryPackingEntry Reverse(int packingEntryId, string? remarks = null)
    {
        var original = _db.AccessoryPackingEntries.Find(packingEntryId)
            ?? throw new DomainException("Accessory packing entry not found.");
        if (original.IsReversal)
            throw new DomainException("A reversal entry cannot itself be reversed.");
        if (_db.AccessoryPackingEntries.Any(e => e.ReversesEntryId == packingEntryId))
            throw new DomainException("This packing entry has already been reversed.");

        // Same "measured by ledger position, not wall-clock time" rule PackingService.Reverse uses,
        // and the same conservative choice of the earliest leg for the entry.
        var packingMovementId = _db.AccessoryStockMovements
            .Where(m => m.SourceType == "AccessoryPackingEntry" && m.SourceId == original.Id)
            .Select(m => (int?)m.Id)
            .Min() ?? 0;

        var dispatchedSince = _db.AccessoryStockMovements.Any(m =>
            m.AccessoryId == original.AccessoryId && m.BrandId == original.BrandId &&
            m.Type == StockMovementType.Dispatch && m.Id > packingMovementId);
        if (dispatchedSince)
            throw new DomainException(
                $"This packing cannot be undone — {Describe(original.AccessoryId)} " +
                $"({BrandName(original.BrandId)}) has been sent out since it was packed. " +
                "Post a new packing entry to correct the split instead.");

        var packedOnHand = _stock.FindAccessoryBalance(original.AccessoryId, original.BrandId)
            ?.PackedOnHand ?? 0m;
        if (packedOnHand < original.Quantity)
            throw new DomainException(
                $"This packing of {original.Quantity:0.###} cannot be undone: only {packedOnHand:0.###} " +
                $"is currently packed for {BrandName(original.BrandId)}.");

        var reversal = new AccessoryPackingEntry
        {
            Date = DateTime.Today,
            AccessoryId = original.AccessoryId,
            BrandId = original.BrandId,
            BatchId = original.BatchId,
            Quantity = original.Quantity,
            Remarks = remarks ?? $"Reversal of packing #{original.Id}",
            IsReversal = true,
            ReversesEntryId = original.Id,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now
        };
        _db.AccessoryPackingEntries.Add(reversal);
        _db.SaveChanges();

        PostMovements(reversal, -original.Quantity, reversal.Remarks);
        _db.SaveChanges();

        return reversal;
    }

    /// <summary>
    /// Writes the two ledger legs one accessory packing line always consists of: unpacked leaves the
    /// shared brand-less pool, packed arrives on the brand's own row. Mirrors
    /// <see cref="PackingService.PostMovements"/>.
    /// </summary>
    private void PostMovements(AccessoryPackingEntry entry, decimal quantity, string? remarks)
    {
        var type = quantity >= 0 ? StockMovementType.Packing : StockMovementType.Adjustment;

        _stock.ApplyAccessory(entry.AccessoryId, brandId: null, type,
            deltaRawOnHand: -quantity, deltaPackedOnHand: 0,
            deltaReserved: 0, entry.Date, "AccessoryPackingEntry", entry.Id, remarks);

        _stock.ApplyAccessory(entry.AccessoryId, entry.BrandId, type,
            deltaRawOnHand: 0, deltaPackedOnHand: quantity,
            deltaReserved: 0, entry.Date, "AccessoryPackingEntry", entry.Id, remarks);
    }

    private string Describe(int accessoryId) =>
        _db.Accessories.Where(x => x.Id == accessoryId).Select(x => x.Name).FirstOrDefault() ?? "accessory";

    private string BrandName(int brandId) =>
        _db.Brands.Where(x => x.Id == brandId).Select(x => x.Name).FirstOrDefault() ?? "?";
}
