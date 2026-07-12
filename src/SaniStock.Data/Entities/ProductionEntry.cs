namespace SaniStock.Data.Entities;

/// <summary>
/// A posting of newly produced finished ware for an item+grade+colour combination.
/// Immutable once posted; corrected via a reversing entry. Increases finished OnHand.
/// </summary>
public class ProductionEntry
{
    public int Id { get; set; }
    public DateTime Date { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int GradeId { get; set; }
    public Grade? Grade { get; set; }

    public int ColourId { get; set; }
    public Colour? Colour { get; set; }

    public decimal Quantity { get; set; }
    public string? Remarks { get; set; }

    /// <summary>True when this row reverses an earlier production entry.</summary>
    public bool IsReversal { get; set; }
    /// <summary>The production entry this one reverses, if any.</summary>
    public int? ReversesEntryId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
