using Microsoft.EntityFrameworkCore;
using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain.Models;

namespace SaniStock.Domain.Services;

/// <summary>
/// Packs produced ware: moves quantity out of the shared unpacked pool for an item+grade+colour
/// and into one or more brands' packed stock. Total on-hand never changes — only the split and
/// which brand owns it — so packing is invisible to the stock total and to every reservation, and
/// simply makes stock ready to ship.
/// <para>
/// Packing is where brand enters the model. One action may be split across several brands (pack
/// 500 as 200/200/100), posting one <see cref="PackingEntry"/> per brand under a shared batch id,
/// so each brand's portion can be undone on its own.
/// </para>
/// </summary>
public class PackingService
{
    private readonly SaniStockDbContext _db;
    private readonly StockService _stock;
    private readonly IUserContext _user;

    public PackingService(SaniStockDbContext db, StockService stock, IUserContext user)
    {
        _db = db;
        _stock = stock;
        _user = user;
    }

    /// <summary>
    /// Posts one packing action, moving quantity from unpacked into the packed stock of each brand
    /// named in <see cref="PackingInput.Lines"/>. The lines' quantities are checked <em>together</em>
    /// against the unpacked balance: a 500-piece pool can be split 200/200/100 across three brands,
    /// but the three cannot add up to 501. Returns the posted entries, one per brand, in input order.
    /// </summary>
    public IReadOnlyList<PackingEntry> Post(PackingInput input)
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
        var rawOnHand = _stock.FindFinishedBalance(input.ItemId, input.GradeId, input.ColourId)
            ?.RawOnHand ?? 0m;
        if (total > rawOnHand)
            throw new DomainException(
                $"Cannot pack {total:0.###} — only {rawOnHand:0.###} is unpacked for " +
                $"{Describe(input.ItemId, input.GradeId, input.ColourId)}. Add stock first.");

        var entries = new List<PackingEntry>();
        foreach (var line in input.Lines)
        {
            var entry = new PackingEntry
            {
                Date = input.Date,
                ItemId = input.ItemId,
                GradeId = input.GradeId,
                ColourId = input.ColourId,
                BrandId = line.BrandId,
                Quantity = line.Quantity,
                Remarks = input.Remarks,
                CreatedBy = _user.Username,
                CreatedAt = DateTime.Now
            };
            _db.PackingEntries.Add(entry);
            entries.Add(entry);
        }
        _db.SaveChanges(); // assign ids so the movements can reference them and the batch can be stamped

        // The batch is named after its first row, which now has an id. Single-brand packings get a
        // batch id too, so the history screen can group every posting the same way.
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
    /// Reverses one brand's packing entry, moving its quantity back out of that brand's packed
    /// stock into the shared unpacked pool and recording a linked reversing entry. The original row
    /// is left untouched (immutable), and the other brands in the same batch are unaffected.
    /// <para>
    /// Blocked once anything has shipped for that item+grade+colour+brand since the packing was
    /// posted: the packed stock this entry created may be exactly what went out, and un-packing it
    /// after the fact would rewrite history. Post a fresh packing entry to correct the split instead.
    /// </para>
    /// </summary>
    public PackingEntry Reverse(int packingEntryId, string? remarks = null)
    {
        var original = _db.PackingEntries.Find(packingEntryId)
            ?? throw new DomainException("Packing entry not found.");
        if (original.IsReversal)
            throw new DomainException("A reversal entry cannot itself be reversed.");
        if (_db.PackingEntries.Any(e => e.ReversesEntryId == packingEntryId))
            throw new DomainException("This packing entry has already been reversed.");

        // "Since" is measured by position in the append-only ledger, not by wall-clock time: ids are
        // monotonic, so this stays correct even for two postings within the same clock tick. A
        // packing writes two movements (the -Raw leg and the +Packed leg) and the Brand migration
        // appended the packed legs of historic entries at the end of the table, so take the
        // *earliest* movement for this entry — the conservative choice, which can only ever refuse
        // a reversal that was borderline, never allow one it should have blocked.
        var packingMovementId = _db.StockMovements
            .Where(m => m.SourceType == "PackingEntry" && m.SourceId == original.Id)
            .Select(m => (int?)m.Id)
            .Min() ?? 0;

        var dispatchedSince = _db.StockMovements.Any(m =>
            m.ItemId == original.ItemId && m.GradeId == original.GradeId &&
            m.ColourId == original.ColourId && m.BrandId == original.BrandId &&
            m.Type == StockMovementType.Dispatch && m.Id > packingMovementId);
        if (dispatchedSince)
            throw new DomainException(
                $"This packing cannot be undone — {Describe(original.ItemId, original.GradeId, original.ColourId)} " +
                $"({BrandName(original.BrandId)}) has been sent out since it was packed. " +
                "Post a new packing entry to correct the split instead.");

        var packedOnHand = _stock
            .FindFinishedBalance(original.ItemId, original.GradeId, original.ColourId, original.BrandId)
            ?.PackedOnHand ?? 0m;
        if (packedOnHand < original.Quantity)
            throw new DomainException(
                $"This packing of {original.Quantity:0.###} cannot be undone: only {packedOnHand:0.###} " +
                $"is currently packed for {BrandName(original.BrandId)}.");

        var reversal = new PackingEntry
        {
            Date = DateTime.Today,
            ItemId = original.ItemId,
            GradeId = original.GradeId,
            ColourId = original.ColourId,
            BrandId = original.BrandId,
            BatchId = original.BatchId,
            Quantity = original.Quantity,
            Remarks = remarks ?? $"Reversal of packing #{original.Id}",
            IsReversal = true,
            ReversesEntryId = original.Id,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now
        };
        _db.PackingEntries.Add(reversal);
        _db.SaveChanges();

        PostMovements(reversal, -original.Quantity, reversal.Remarks);
        _db.SaveChanges();

        return reversal;
    }

    /// <summary>
    /// Writes the two ledger legs one packing line always consists of: unpacked leaves the shared
    /// brand-less pool, packed arrives on the brand's own row. They have to be separate movements
    /// because a movement belongs to exactly one balance row — which is also what lets
    /// <see cref="StockService.ReconcileAll"/> rebuild the per-brand split from the ledger alone.
    /// A negative <paramref name="quantity"/> runs the pair backwards for a reversal.
    /// </summary>
    private void PostMovements(PackingEntry entry, decimal quantity, string? remarks)
    {
        // A reversal is a correction, not a packing, so it is typed as an Adjustment — matching how
        // production reversals are recorded.
        var type = quantity >= 0 ? StockMovementType.Packing : StockMovementType.Adjustment;

        _stock.ApplyFinished(entry.ItemId, entry.GradeId, entry.ColourId, brandId: null, type,
            deltaRawOnHand: -quantity, deltaPackedOnHand: 0,
            deltaReserved: 0, entry.Date, "PackingEntry", entry.Id, remarks);

        _stock.ApplyFinished(entry.ItemId, entry.GradeId, entry.ColourId, entry.BrandId, type,
            deltaRawOnHand: 0, deltaPackedOnHand: quantity,
            deltaReserved: 0, entry.Date, "PackingEntry", entry.Id, remarks);
    }

    /// <summary>A readable "Item / Grade / Colour" label for error messages.</summary>
    private string Describe(int itemId, int gradeId, int colourId)
    {
        var item = _db.Items.Where(x => x.Id == itemId).Select(x => x.Name).FirstOrDefault() ?? "item";
        var grade = _db.Grades.Where(x => x.Id == gradeId).Select(x => x.Name).FirstOrDefault() ?? "?";
        var colour = _db.Colours.Where(x => x.Id == colourId).Select(x => x.Name).FirstOrDefault() ?? "?";
        return $"{item} / {grade} / {colour}";
    }

    private string BrandName(int brandId) =>
        _db.Brands.Where(x => x.Id == brandId).Select(x => x.Name).FirstOrDefault() ?? "?";
}
