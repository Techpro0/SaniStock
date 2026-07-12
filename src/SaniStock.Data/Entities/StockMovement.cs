namespace SaniStock.Data.Entities;

/// <summary>
/// An immutable, signed ledger entry for finished-goods stock (item+grade+colour).
/// The cached <see cref="StockBalance"/> is the running sum of these deltas and can
/// always be rebuilt from the ledger. This is the single source of truth for stock math.
/// </summary>
public class StockMovement
{
    public int Id { get; set; }

    /// <summary>Business date the movement takes effect (used for as-of-date reports).</summary>
    public DateTime Date { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int GradeId { get; set; }
    public Grade? Grade { get; set; }

    public int ColourId { get; set; }
    public Colour? Colour { get; set; }

    public StockMovementType Type { get; set; }

    /// <summary>Signed change to on-hand quantity (+production, -dispatch).</summary>
    public decimal DeltaOnHand { get; set; }

    /// <summary>Signed change to reserved quantity (+booking, -release, -dispatch).</summary>
    public decimal DeltaReserved { get; set; }

    /// <summary>Originating document type, e.g. "ProductionEntry", "Order", "DispatchEntry".</summary>
    public string? SourceType { get; set; }
    /// <summary>Originating document id, for traceability.</summary>
    public int? SourceId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? Remarks { get; set; }
}
