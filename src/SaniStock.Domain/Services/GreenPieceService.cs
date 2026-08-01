using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain.Models;

namespace SaniStock.Domain.Services;

/// <summary>
/// Records green (unfired) ware in/out and maintains its balance.
/// <para>
/// Entries are immutable: a mistake is corrected by <see cref="Reverse"/>, which writes a linked
/// row carrying the opposite direction. Because a reversal is itself just another signed entry,
/// the entries for an item+colour always sum to that combination's balance — which is what lets
/// <see cref="StockService.ReconcileAll"/> rebuild it.
/// </para>
/// </summary>
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

    /// <summary>
    /// Posts a green-ware receipt or issue. An issue cannot take out more than is on hand — green
    /// ware is a count of physical unfired pieces, and a negative count is never a real state.
    /// </summary>
    public GreenPieceEntry Post(GreenPieceInput input)
    {
        if (input.Quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        var bal = GetOrCreateBalance(input.ItemId, input.ColourId);
        if (input.IsIssue && input.Quantity > bal.OnHand)
            throw new DomainException(
                $"Cannot issue {input.Quantity:0.###} — only {bal.OnHand:0.###} of " +
                $"{Describe(input.ItemId, input.ColourId)} is in stock as green ware.");

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

        Apply(bal, input.IsIssue, input.Quantity, input.ItemId, input.ColourId);
        _db.SaveChanges();
        return entry;
    }

    /// <summary>
    /// Reverses an entry by posting its mirror image — an issue undoes a receipt and vice versa —
    /// and recording a linked reversing row. The original is left untouched.
    /// <para>
    /// Reversing a <em>receipt</em> takes green ware back out, so it is refused when the balance no
    /// longer covers it: those pieces have since been issued (fired, most likely) and un-receiving
    /// them would rewrite history. Reversing an <em>issue</em> only ever adds back, so it is always
    /// allowed.
    /// </para>
    /// </summary>
    public GreenPieceEntry Reverse(int entryId, string? remarks = null)
    {
        var original = _db.GreenPieceEntries.Find(entryId)
            ?? throw new DomainException("Green ware entry not found.");
        if (original.IsReversal)
            throw new DomainException("A reversal entry cannot itself be reversed.");
        if (_db.GreenPieceEntries.Any(e => e.ReversesEntryId == entryId))
            throw new DomainException("This entry has already been reversed.");

        // The reversal runs the original backwards.
        var reversalIsIssue = !original.IsIssue;

        var bal = GetOrCreateBalance(original.ItemId, original.ColourId);
        if (reversalIsIssue && original.Quantity > bal.OnHand)
            throw new DomainException(
                $"This receipt of {original.Quantity:0.###} cannot be reversed: only " +
                $"{bal.OnHand:0.###} of {Describe(original.ItemId, original.ColourId)} is still " +
                "in stock as green ware.");

        var reversal = new GreenPieceEntry
        {
            Date = DateTime.Today,
            ItemId = original.ItemId,
            ColourId = original.ColourId,
            IsIssue = reversalIsIssue,
            Quantity = original.Quantity,
            Remarks = remarks ?? $"Reversal of green ware entry #{original.Id}",
            IsReversal = true,
            ReversesEntryId = original.Id,
            CreatedBy = _user.Username,
            CreatedAt = DateTime.Now
        };
        _db.GreenPieceEntries.Add(reversal);

        Apply(bal, reversalIsIssue, original.Quantity, original.ItemId, original.ColourId);
        _db.SaveChanges();
        return reversal;
    }

    private void Apply(GreenPieceBalance bal, bool isIssue, decimal quantity, int itemId, int colourId)
    {
        var before = bal.OnHand;
        bal.OnHand += isIssue ? -quantity : quantity;

        _stock.AddAudit(isIssue ? "GreenIssue" : "GreenReceipt", "GreenPieceBalance", null,
            $"Item {itemId}/C{colourId}: OnHand {before}->{bal.OnHand}", before, bal.OnHand);
    }

    private string Describe(int itemId, int colourId)
    {
        var item = _db.Items.Where(x => x.Id == itemId).Select(x => x.Name).FirstOrDefault() ?? "item";
        var colour = _db.Colours.Where(x => x.Id == colourId).Select(x => x.Name).FirstOrDefault() ?? "?";
        return $"{item} / {colour}";
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
