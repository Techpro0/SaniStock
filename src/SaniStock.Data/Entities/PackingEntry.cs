namespace SaniStock.Data.Entities;

/// <summary>
/// A posting that packs already-produced ware under one brand: moves quantity from the shared
/// unpacked pool for an item+grade+colour into that brand's packed stock. Total on-hand is
/// unchanged — only the split, and which brand now owns it. Immutable once posted; corrected via
/// a reversing entry.
/// <para>
/// A single packing action may be split across several brands (pack 500 as 200/200/100 for three
/// brands). That posts one row <em>per brand</em>, all sharing a <see cref="BatchId"/>, rather than
/// a header with child lines. Keeping each brand-line a whole <c>PackingEntry</c> means reversal
/// keeps its existing shape — <see cref="IsReversal"/> / <see cref="ReversesEntryId"/> keep their
/// current meaning and the movements' <c>SourceType</c>/<c>SourceId</c> traceability is unchanged —
/// so undoing one brand's portion never forces undoing the others. The cost is that no single row
/// owns "the packing action": the remark is repeated across the batch, and undoing a whole batch is
/// a loop over its rows.
/// </para>
/// </summary>
public class PackingEntry
{
    public int Id { get; set; }
    public DateTime Date { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int GradeId { get; set; }
    public Grade? Grade { get; set; }

    public int ColourId { get; set; }
    public Colour? Colour { get; set; }

    /// <summary>The brand this quantity was packed under. Required — packing is what assigns brand.</summary>
    public int BrandId { get; set; }
    public Brand? Brand { get; set; }

    /// <summary>
    /// Groups the rows posted by one multi-brand packing action, set to the id of the batch's first
    /// row. Null on rows written before batching existed, so read it as <c>BatchId ?? Id</c>.
    /// </summary>
    public int? BatchId { get; set; }

    public decimal Quantity { get; set; }
    public string? Remarks { get; set; }

    /// <summary>True when this row reverses an earlier packing entry.</summary>
    public bool IsReversal { get; set; }
    /// <summary>The packing entry this one reverses, if any.</summary>
    public int? ReversesEntryId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
