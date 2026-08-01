namespace SaniStock.Data.Entities;

/// <summary>
/// A dispatch note against an order. Reduces on-hand and reserved for the lines shipped.
/// <para>
/// Immutable once posted, like every other posting here: a mistake is corrected by
/// <c>DispatchService.Reverse</c>, which writes a linked reversing note rather than editing or
/// removing this one.
/// </para>
/// <para>
/// <b>Where the goods came from is not repeated on these rows.</b> A single shipped line can span
/// several grades (cross-grade borrowing) and, within each, a brand's packed stock and the shared
/// unpacked pool — so a per-line "from packed / from raw" pair cannot express it. The
/// <see cref="StockMovement"/> rows this dispatch wrote already record the exact split per
/// item+grade+colour+brand, and they are the source of truth the balances are rebuilt from.
/// Reversal reads them back and inverts them.
/// </para>
/// </summary>
public class DispatchEntry
{
    public int Id { get; set; }
    public string DispatchNo { get; set; } = string.Empty;

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public DateTime Date { get; set; }
    public string DispatchedBy { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>True when this note reverses an earlier dispatch, putting the goods back.</summary>
    public bool IsReversal { get; set; }

    /// <summary>The dispatch this one reverses, if any.</summary>
    public int? ReversesEntryId { get; set; }

    public List<DispatchLine> Lines { get; set; } = new();
    public List<DispatchAccessoryLine> AccessoryLines { get; set; } = new();
}
