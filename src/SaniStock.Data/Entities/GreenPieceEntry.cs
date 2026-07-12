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

    public string? Remarks { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
