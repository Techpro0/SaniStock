namespace SaniStock.Data.Entities;

/// <summary>Cached on-hand/reserved balance for an accessory.</summary>
public class AccessoryStockBalance
{
    public int Id { get; set; }

    public int AccessoryId { get; set; }
    public Accessory? Accessory { get; set; }

    public decimal OnHand { get; set; }
    public decimal Reserved { get; set; }

    public decimal Available => OnHand - Reserved;
}
