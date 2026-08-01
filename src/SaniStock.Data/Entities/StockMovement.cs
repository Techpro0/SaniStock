namespace SaniStock.Data.Entities;

/// <summary>
/// An immutable, signed ledger entry for finished-goods stock (item+grade+colour+brand).
/// The cached <see cref="StockBalance"/> is the running sum of these deltas and can
/// always be rebuilt from the ledger. This is the single source of truth for stock math.
/// <para>
/// A movement applies to exactly one balance row, so an action that touches both the brand-less
/// unpacked pool and a brand's packed stock writes <em>two</em> movements. Packing 200 as Brand A
/// is a -Raw leg on the brand-less key plus a +Packed leg on the Brand A key; both carry
/// <see cref="StockMovementType.Packing"/> and the same source document.
/// </para>
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

    /// <summary>
    /// Null for the shared unpacked pool (all production, and the -Raw leg of packing); set for a
    /// brand's packed stock. Recorded for traceability and so <c>ReconcileAll</c> can rebuild the
    /// per-brand split from the ledger alone.
    /// </summary>
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public StockMovementType Type { get; set; }

    /// <summary>Signed change to unpacked on-hand quantity (+production, -packing, -dispatch).</summary>
    public decimal DeltaRawOnHand { get; set; }

    /// <summary>Signed change to packed on-hand quantity (+packing, -dispatch).</summary>
    public decimal DeltaPackedOnHand { get; set; }

    /// <summary>
    /// Net signed change to total on-hand. Derived from the two bucket deltas, so a packing
    /// movement (-raw/+packed) correctly nets to zero.
    /// </summary>
    public decimal DeltaOnHand => DeltaRawOnHand + DeltaPackedOnHand;

    /// <summary>Signed change to reserved quantity (+booking, -release, -dispatch).</summary>
    public decimal DeltaReserved { get; set; }

    /// <summary>Originating document type, e.g. "ProductionEntry", "PackingEntry", "Order", "DispatchEntry".</summary>
    public string? SourceType { get; set; }
    /// <summary>Originating document id, for traceability.</summary>
    public int? SourceId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? Remarks { get; set; }
}
