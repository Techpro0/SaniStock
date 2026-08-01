using Microsoft.EntityFrameworkCore;
using SaniStock.Data;
using SaniStock.Data.Entities;

namespace SaniStock.Domain.Services;

/// <summary>
/// The single choke-point for all finished-goods and accessory stock changes.
/// Every change appends an immutable signed movement AND updates the cached balance,
/// and writes an audit row with before/after quantities. Callers stage changes and
/// then commit with a single <see cref="SaniStockDbContext.SaveChanges()"/> so each
/// business operation is atomic. Balances are always rebuildable from the ledger via
/// <see cref="ReconcileAll"/>.
/// </summary>
public class StockService
{
    private readonly SaniStockDbContext _db;
    private readonly IUserContext _user;

    public StockService(SaniStockDbContext db, IUserContext user)
    {
        _db = db;
        _user = user;
    }

    // ---- Finished goods ------------------------------------------------------

    /// <summary>
    /// Finds the balance row for a key, checking the change-tracker first. Returns null when the
    /// combination has never been stocked — use this for read-only checks so a failed validation
    /// never leaves an empty balance row staged.
    /// <para>
    /// <paramref name="brandId"/> null selects the shared unpacked pool for the item+grade+colour;
    /// a brand id selects that brand's packed stock. They are different rows and never mix.
    /// </para>
    /// </summary>
    public StockBalance? FindFinishedBalance(int itemId, int gradeId, int colourId, int? brandId = null)
    {
        var local = _db.StockBalances.Local
            .FirstOrDefault(x => x.ItemId == itemId && x.GradeId == gradeId && x.ColourId == colourId
                                 && x.BrandId == brandId);
        if (local != null) return local;

        return _db.StockBalances
            .FirstOrDefault(x => x.ItemId == itemId && x.GradeId == gradeId && x.ColourId == colourId
                                 && x.BrandId == brandId);
    }

    /// <summary>Finds the balance row for a key, checking the change-tracker first, else creating it.</summary>
    public StockBalance GetOrCreateFinishedBalance(int itemId, int gradeId, int colourId, int? brandId = null)
    {
        var existing = FindFinishedBalance(itemId, gradeId, colourId, brandId);
        if (existing != null) return existing;

        var created = new StockBalance
        {
            ItemId = itemId,
            GradeId = gradeId,
            ColourId = colourId,
            BrandId = brandId
        };
        _db.StockBalances.Add(created);
        return created;
    }

    /// <summary>
    /// Appends a finished-goods movement and applies its signed deltas to the cached balance.
    /// On-hand is moved per bucket: production adds to raw, packing shifts raw to packed, dispatch
    /// removes from both. Does not save; the caller controls the transaction boundary.
    /// <para>
    /// One call touches one balance row, identified by item+grade+colour+brand. An action spanning
    /// the unpacked pool and a brand's packed stock — packing, its reversal, a dispatch that has to
    /// fall back to the sibling row — makes two calls and writes two movements.
    /// </para>
    /// </summary>
    public void ApplyFinished(int itemId, int gradeId, int colourId, int? brandId, StockMovementType type,
        decimal deltaRawOnHand, decimal deltaPackedOnHand, decimal deltaReserved, DateTime date,
        string? sourceType, int? sourceId, string? remarks)
    {
        GuardBucketBrandInvariant(brandId, deltaRawOnHand, deltaPackedOnHand);

        var bal = GetOrCreateFinishedBalance(itemId, gradeId, colourId, brandId);
        var beforeOnHand = bal.OnHand;
        var beforeRaw = bal.RawOnHand;
        var beforePacked = bal.PackedOnHand;
        var beforeReserved = bal.Reserved;

        bal.RawOnHand += deltaRawOnHand;
        bal.PackedOnHand += deltaPackedOnHand;
        bal.Reserved += deltaReserved;

        _db.StockMovements.Add(new StockMovement
        {
            Date = date,
            ItemId = itemId,
            GradeId = gradeId,
            ColourId = colourId,
            BrandId = brandId,
            Type = type,
            DeltaRawOnHand = deltaRawOnHand,
            DeltaPackedOnHand = deltaPackedOnHand,
            DeltaReserved = deltaReserved,
            SourceType = sourceType,
            SourceId = sourceId,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now,
            Remarks = remarks
        });

        AddAudit(type.ToString(), "StockBalance", sourceId,
            $"Item {itemId}/G{gradeId}/C{colourId}/B{(brandId?.ToString() ?? "-")}: " +
            $"Unpacked {beforeRaw}->{bal.RawOnHand}, " +
            $"Packed {beforePacked}->{bal.PackedOnHand}, Reserved {beforeReserved}->{bal.Reserved}",
            beforeOnHand, bal.OnHand);
    }

    /// <summary>
    /// Upholds the split that brand imposes on a balance row: unpacked stock is brand-less and
    /// shared, packed stock always belongs to a brand. A movement that would put packed quantity on
    /// the brand-less row, or unpacked quantity on a brand's row, is a bug in a calling service —
    /// it would make the two rows disagree about where the goods physically are, and no report
    /// could reconcile them afterwards.
    /// <para>
    /// Checked here, at the single place movements are written, rather than as a database check
    /// constraint: balances are a cache that <see cref="ReconcileAll"/> rewrites wholesale, and a
    /// constraint would turn a ledger anomaly into a crash during the very repair meant to expose
    /// it. Reserved is unconstrained — both rows legitimately hold reservations.
    /// </para>
    /// </summary>
    private static void GuardBucketBrandInvariant(int? brandId, decimal deltaRawOnHand, decimal deltaPackedOnHand)
    {
        if (brandId is null && deltaPackedOnHand != 0)
            throw new DomainException(
                "Packed stock must belong to a brand: a brand-less stock movement cannot change the packed " +
                "quantity. Post the packed side of the movement against the brand it was packed under.");

        if (brandId is not null && deltaRawOnHand != 0)
            throw new DomainException(
                "Unpacked stock is shared across brands: a brand's stock movement cannot change the unpacked " +
                "quantity. Post the unpacked side of the movement against the brand-less pool.");
    }

    // ---- Bucket math ---------------------------------------------------------

    /// <summary>
    /// Unreserved quantity on a single balance row. Because brand splits packed stock onto its own
    /// row, each row holds exactly one bucket and exactly the reservations drawn from it, so this
    /// is a plain subtraction — no assumption about which bucket a reservation "really" consumed.
    /// </summary>
    public static decimal FreeOn(StockBalance? bal) =>
        bal is null ? 0m : Math.Max(0m, bal.OnHand - bal.Reserved);

    // ---- Accessories ---------------------------------------------------------

    public AccessoryStockBalance GetOrCreateAccessoryBalance(int accessoryId)
    {
        var local = _db.AccessoryStockBalances.Local.FirstOrDefault(x => x.AccessoryId == accessoryId);
        if (local != null) return local;

        var existing = _db.AccessoryStockBalances.FirstOrDefault(x => x.AccessoryId == accessoryId);
        if (existing != null) return existing;

        var created = new AccessoryStockBalance { AccessoryId = accessoryId };
        _db.AccessoryStockBalances.Add(created);
        return created;
    }

    /// <summary>Accessory analogue of <see cref="ApplyFinished"/>.</summary>
    public void ApplyAccessory(int accessoryId, StockMovementType type,
        decimal deltaOnHand, decimal deltaReserved, DateTime date,
        string? sourceType, int? sourceId, string? remarks)
    {
        var bal = GetOrCreateAccessoryBalance(accessoryId);
        var beforeOnHand = bal.OnHand;
        var beforeReserved = bal.Reserved;

        bal.OnHand += deltaOnHand;
        bal.Reserved += deltaReserved;

        _db.AccessoryStockMovements.Add(new AccessoryStockMovement
        {
            Date = date,
            AccessoryId = accessoryId,
            Type = type,
            DeltaOnHand = deltaOnHand,
            DeltaReserved = deltaReserved,
            SourceType = sourceType,
            SourceId = sourceId,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now,
            Remarks = remarks
        });

        AddAudit(type.ToString(), "AccessoryStockBalance", sourceId,
            $"Accessory {accessoryId}: OnHand {beforeOnHand}->{bal.OnHand}, Reserved {beforeReserved}->{bal.Reserved}",
            beforeOnHand, bal.OnHand);
    }

    // ---- Audit ---------------------------------------------------------------

    /// <summary>Stages an audit row (saved with the surrounding operation).</summary>
    public void AddAudit(string action, string entityType, int? entityId, string details,
        decimal? before = null, decimal? after = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTime.Now,
            Username = _user.Username,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            QuantityBefore = before,
            QuantityAfter = after
        });
    }

    // ---- Reconciliation ------------------------------------------------------

    /// <summary>
    /// Rebuilds every cached balance from the immutable ledgers. Used after a restore
    /// or to verify integrity. Returns the number of balance rows written.
    /// <para>
    /// The two on-hand buckets rebuild independently, each as the running sum of its own signed
    /// delta, so no knowledge of packing or allocation order is needed here: production carries
    /// +raw, packing carries -raw/+packed, and dispatch carries whatever split it actually drew.
    /// Grouping includes brand, so the per-brand packed split rebuilds from the ledger too — the
    /// two legs of a packing entry land on their own keys and stay there.
    /// </para>
    /// </summary>
    public int ReconcileAll()
    {
        var finished = _db.StockMovements
            .GroupBy(m => new { m.ItemId, m.GradeId, m.ColourId, m.BrandId })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.GradeId,
                g.Key.ColourId,
                g.Key.BrandId,
                RawOnHand = g.Sum(x => x.DeltaRawOnHand),
                PackedOnHand = g.Sum(x => x.DeltaPackedOnHand),
                Reserved = g.Sum(x => x.DeltaReserved)
            })
            .ToList();

        foreach (var f in finished)
        {
            var bal = GetOrCreateFinishedBalance(f.ItemId, f.GradeId, f.ColourId, f.BrandId);
            bal.RawOnHand = f.RawOnHand;
            bal.PackedOnHand = f.PackedOnHand;
            bal.Reserved = f.Reserved;
        }

        var accessories = _db.AccessoryStockMovements
            .GroupBy(m => m.AccessoryId)
            .Select(g => new
            {
                AccessoryId = g.Key,
                OnHand = g.Sum(x => x.DeltaOnHand),
                Reserved = g.Sum(x => x.DeltaReserved)
            })
            .ToList();

        foreach (var a in accessories)
        {
            var bal = GetOrCreateAccessoryBalance(a.AccessoryId);
            bal.OnHand = a.OnHand;
            bal.Reserved = a.Reserved;
        }

        var written = _db.SaveChanges();
        return finished.Count + accessories.Count;
    }
}
