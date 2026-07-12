namespace SaniStock.Data.Entities;

/// <summary>
/// A stock-in of accessories (purchased/received), analogous to a production entry for
/// finished ware. Immutable; corrected via a reversing entry. Increases accessory OnHand.
/// </summary>
public class AccessoryReceipt
{
    public int Id { get; set; }
    public DateTime Date { get; set; }

    public int AccessoryId { get; set; }
    public Accessory? Accessory { get; set; }

    public decimal Quantity { get; set; }
    public string? Remarks { get; set; }

    public bool IsReversal { get; set; }
    public int? ReversesEntryId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
