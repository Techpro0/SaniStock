using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain.Models;

namespace SaniStock.Domain.Services;

/// <summary>
/// Records raw-material in/out and maintains its balance.
/// <para>
/// Entries are immutable: a mistake is corrected by <see cref="Reverse"/>, which writes a linked
/// row carrying the opposite direction. Because a reversal is itself just another signed entry,
/// the entries for a material always sum to its balance — which is what lets
/// <see cref="StockService.ReconcileAll"/> rebuild it.
/// </para>
/// </summary>
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

    /// <summary>
    /// Posts a raw-material receipt or issue. An issue cannot take out more than is on hand — you
    /// cannot consume clay you do not have, and a negative balance would misstate every later one.
    /// </summary>
    public RawMaterialEntry Post(RawMaterialInput input)
    {
        if (input.Quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        var bal = GetOrCreateBalance(input.RawMaterialId);
        if (input.IsIssue && input.Quantity > bal.OnHand)
            throw new DomainException(
                $"Cannot issue {input.Quantity:0.###} — only {bal.OnHand:0.###} of " +
                $"{Describe(input.RawMaterialId)} is in stock.");

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

        Apply(bal, input.IsIssue, input.Quantity, input.RawMaterialId);
        _db.SaveChanges();
        return entry;
    }

    /// <summary>
    /// Reverses an entry by posting its mirror image — an issue undoes a receipt and vice versa —
    /// and recording a linked reversing row. The original is left untouched.
    /// <para>
    /// Reversing a <em>receipt</em> takes material back out, so it is refused when the balance no
    /// longer covers it: it has since been consumed. Reversing an <em>issue</em> only ever adds
    /// back, so it is always allowed.
    /// </para>
    /// </summary>
    public RawMaterialEntry Reverse(int entryId, string? remarks = null)
    {
        var original = _db.RawMaterialEntries.Find(entryId)
            ?? throw new DomainException("Raw material entry not found.");
        if (original.IsReversal)
            throw new DomainException("A reversal entry cannot itself be reversed.");
        if (_db.RawMaterialEntries.Any(e => e.ReversesEntryId == entryId))
            throw new DomainException("This entry has already been reversed.");

        // The reversal runs the original backwards.
        var reversalIsIssue = !original.IsIssue;

        var bal = GetOrCreateBalance(original.RawMaterialId);
        if (reversalIsIssue && original.Quantity > bal.OnHand)
            throw new DomainException(
                $"This receipt of {original.Quantity:0.###} cannot be reversed: only " +
                $"{bal.OnHand:0.###} of {Describe(original.RawMaterialId)} is still in stock.");

        var reversal = new RawMaterialEntry
        {
            Date = DateTime.Today,
            RawMaterialId = original.RawMaterialId,
            IsIssue = reversalIsIssue,
            Quantity = original.Quantity,
            Remarks = remarks ?? $"Reversal of raw material entry #{original.Id}",
            IsReversal = true,
            ReversesEntryId = original.Id,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now
        };
        _db.RawMaterialEntries.Add(reversal);

        Apply(bal, reversalIsIssue, original.Quantity, original.RawMaterialId);
        _db.SaveChanges();
        return reversal;
    }

    private void Apply(RawMaterialBalance bal, bool isIssue, decimal quantity, int rawMaterialId)
    {
        var before = bal.OnHand;
        bal.OnHand += isIssue ? -quantity : quantity;

        _stock.AddAudit(isIssue ? "RawIssue" : "RawReceipt", "RawMaterialBalance", null,
            $"RawMaterial {rawMaterialId}: OnHand {before}->{bal.OnHand}", before, bal.OnHand);
    }

    private string Describe(int rawMaterialId) =>
        _db.RawMaterials.Where(x => x.Id == rawMaterialId).Select(x => x.Name).FirstOrDefault() ?? "this material";

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
