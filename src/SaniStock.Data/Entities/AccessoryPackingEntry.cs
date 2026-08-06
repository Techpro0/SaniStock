namespace SaniStock.Data.Entities;

/// <summary>
/// A posting that packs already-received accessory stock under one brand: moves quantity from the
/// shared unpacked pool for an accessory into that brand's packed stock. Mirrors
/// <see cref="PackingEntry"/> for finished ware, minus grade/colour — accessories have neither.
/// Total on-hand is unchanged; only the split, and which brand now owns it. Immutable once posted;
/// corrected via a reversing entry.
/// <para>
/// A single packing action may be split across several brands, posting one row <em>per brand</em>
/// sharing a <see cref="BatchId"/>, exactly as <see cref="PackingEntry"/> does — so undoing one
/// brand's portion never forces undoing the others.
/// </para>
/// </summary>
public class AccessoryPackingEntry
{
    public int Id { get; set; }
    public DateTime Date { get; set; }

    public int AccessoryId { get; set; }
    public Accessory? Accessory { get; set; }

    /// <summary>The brand this quantity was packed under. Required — packing is what assigns brand.</summary>
    public int BrandId { get; set; }
    public Brand? Brand { get; set; }

    /// <summary>
    /// Groups the rows posted by one multi-brand packing action, set to the id of the batch's first
    /// row.
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
