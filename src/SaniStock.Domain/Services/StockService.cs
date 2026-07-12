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

    /// <summary>Finds the balance row for a key, checking the change-tracker first, else creating it.</summary>
    public StockBalance GetOrCreateFinishedBalance(int itemId, int gradeId, int colourId)
    {
        var local = _db.StockBalances.Local
            .FirstOrDefault(x => x.ItemId == itemId && x.GradeId == gradeId && x.ColourId == colourId);
        if (local != null) return local;

        var existing = _db.StockBalances
            .FirstOrDefault(x => x.ItemId == itemId && x.GradeId == gradeId && x.ColourId == colourId);
        if (existing != null) return existing;

        var created = new StockBalance { ItemId = itemId, GradeId = gradeId, ColourId = colourId };
        _db.StockBalances.Add(created);
        return created;
    }

    /// <summary>
    /// Appends a finished-goods movement and applies its signed deltas to the cached balance.
    /// Does not save; the caller controls the transaction boundary.
    /// </summary>
    public void ApplyFinished(int itemId, int gradeId, int colourId, StockMovementType type,
        decimal deltaOnHand, decimal deltaReserved, DateTime date,
        string? sourceType, int? sourceId, string? remarks)
    {
        var bal = GetOrCreateFinishedBalance(itemId, gradeId, colourId);
        var beforeOnHand = bal.OnHand;
        var beforeReserved = bal.Reserved;

        bal.OnHand += deltaOnHand;
        bal.Reserved += deltaReserved;

        _db.StockMovements.Add(new StockMovement
        {
            Date = date,
            ItemId = itemId,
            GradeId = gradeId,
            ColourId = colourId,
            Type = type,
            DeltaOnHand = deltaOnHand,
            DeltaReserved = deltaReserved,
            SourceType = sourceType,
            SourceId = sourceId,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now,
            Remarks = remarks
        });

        AddAudit(type.ToString(), "StockBalance", sourceId,
            $"Item {itemId}/G{gradeId}/C{colourId}: OnHand {beforeOnHand}->{bal.OnHand}, Reserved {beforeReserved}->{bal.Reserved}",
            beforeOnHand, bal.OnHand);
    }

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
    /// </summary>
    public int ReconcileAll()
    {
        var finished = _db.StockMovements
            .GroupBy(m => new { m.ItemId, m.GradeId, m.ColourId })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.GradeId,
                g.Key.ColourId,
                OnHand = g.Sum(x => x.DeltaOnHand),
                Reserved = g.Sum(x => x.DeltaReserved)
            })
            .ToList();

        foreach (var f in finished)
        {
            var bal = GetOrCreateFinishedBalance(f.ItemId, f.GradeId, f.ColourId);
            bal.OnHand = f.OnHand;
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
