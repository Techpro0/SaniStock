namespace SaniStock.Data.Entities;

/// <summary>
/// A stock-in of accessories (purchased/received), analogous to a production entry for
/// finished ware. Immutable; corrected via a reversing entry. Increases accessory OnHand.
/// <para>
/// <see cref="BrandId"/> is null for the ordinary case — the receipt lands in the shared unpacked
/// pool, same as before brand existed for accessories. Set it when the goods arrived already
/// packaged under a brand (an outside import, for instance): the receipt then lands directly on
/// that brand's packed stock, skipping a separate trip to the Packing screen to assign it.
/// </para>
/// </summary>
public class AccessoryReceipt
{
    public int Id { get; set; }
    public DateTime Date { get; set; }

    public int AccessoryId { get; set; }
    public Accessory? Accessory { get; set; }

    /// <summary>Null lands the receipt in the shared unpacked pool; set packs it under that brand immediately.</summary>
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public decimal Quantity { get; set; }
    public string? Remarks { get; set; }

    public bool IsReversal { get; set; }
    public int? ReversesEntryId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
