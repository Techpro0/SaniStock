namespace SaniStock.Data.Entities;

/// <summary>
/// A raw-material in/out movement. Each entry is an immutable signed ledger row;
/// the raw-material balance is their running sum.
/// </summary>
public class RawMaterialEntry
{
    public int Id { get; set; }
    public DateTime Date { get; set; }

    public int RawMaterialId { get; set; }
    public RawMaterial? RawMaterial { get; set; }

    /// <summary>True for consumption/issue (out); false for a receipt/purchase (in).</summary>
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
