namespace SaniStock.Data.Entities;

/// <summary>
/// A movement of unfired ("green") ware, keyed by item+colour (no grade until fired).
/// Each entry is an immutable signed ledger row; the green balance is their running sum.
/// </summary>
public class GreenPieceEntry
{
    public int Id { get; set; }
    public DateTime Date { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int ColourId { get; set; }
    public Colour? Colour { get; set; }

    /// <summary>True for an issue/consumption (out); false for a receipt/casting (in).</summary>
    public bool IsIssue { get; set; }

    /// <summary>Always positive; direction is given by <see cref="IsIssue"/>.</summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// True when this row undoes an earlier entry. A reversal carries the <em>opposite</em>
    /// <see cref="IsIssue"/> to the row it reverses, so the entries still sum to the balance and
    /// nothing has to be edited or removed to correct a mistake.
    /// </summary>
    public bool IsReversal { get; set; }

    /// <summary>The entry this one reverses, if any.</summary>
    public int? ReversesEntryId { get; set; }

    public string? Remarks { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
