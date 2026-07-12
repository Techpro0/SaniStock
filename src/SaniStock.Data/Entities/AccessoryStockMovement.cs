namespace SaniStock.Data.Entities;

/// <summary>Immutable signed ledger entry for accessory stock. Mirrors <see cref="StockMovement"/>.</summary>
public class AccessoryStockMovement
{
    public int Id { get; set; }
    public DateTime Date { get; set; }

    public int AccessoryId { get; set; }
    public Accessory? Accessory { get; set; }

    public StockMovementType Type { get; set; }
    public decimal DeltaOnHand { get; set; }
    public decimal DeltaReserved { get; set; }

    public string? SourceType { get; set; }
    public int? SourceId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? Remarks { get; set; }
}
