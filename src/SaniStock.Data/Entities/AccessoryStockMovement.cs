namespace SaniStock.Data.Entities;

/// <summary>
/// Immutable signed ledger entry for accessory stock. Mirrors <see cref="StockMovement"/>: on-hand
/// moves per bucket (<see cref="DeltaRawOnHand"/> / <see cref="DeltaPackedOnHand"/>), and
/// <see cref="BrandId"/> is null for the shared unpacked pool, set for a brand's packed stock.
/// </summary>
public class AccessoryStockMovement
{
    public int Id { get; set; }
    public DateTime Date { get; set; }

    public int AccessoryId { get; set; }
    public Accessory? Accessory { get; set; }

    /// <summary>Null on the shared unpacked pool; set on a brand's packed stock.</summary>
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public StockMovementType Type { get; set; }

    /// <summary>Received but not yet packed. Signed.</summary>
    public decimal DeltaRawOnHand { get; set; }

    /// <summary>Packed and ready to ship. Signed.</summary>
    public decimal DeltaPackedOnHand { get; set; }

    /// <summary>Total change in on-hand. Derived, never stored.</summary>
    public decimal DeltaOnHand => DeltaRawOnHand + DeltaPackedOnHand;

    public decimal DeltaReserved { get; set; }

    public string? SourceType { get; set; }
    public int? SourceId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? Remarks { get; set; }
}
