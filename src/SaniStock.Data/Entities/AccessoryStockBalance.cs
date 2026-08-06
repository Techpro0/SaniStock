namespace SaniStock.Data.Entities;

/// <summary>
/// Cached on-hand/reserved balance for an accessory+brand combination. Maintained incrementally by
/// the stock service and rebuildable from <see cref="AccessoryStockMovement"/>. Mirrors
/// <see cref="StockBalance"/>: on-hand is split into unpacked (<see cref="RawOnHand"/>) and packed
/// (<see cref="PackedOnHand"/>), and only packing moves quantity between the two.
/// <para>
/// <b>Brand splits the row.</b> <see cref="BrandId"/> is null on the shared <em>unpacked</em> pool
/// for the accessory, and set on one row per brand holding that brand's <em>packed</em> stock.
/// </para>
/// </summary>
public class AccessoryStockBalance
{
    public int Id { get; set; }

    public int AccessoryId { get; set; }
    public Accessory? Accessory { get; set; }

    /// <summary>Null on the shared unpacked pool; set on a brand's packed stock.</summary>
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    /// <summary>Received but not yet packed. Always 0 on a branded row.</summary>
    public decimal RawOnHand { get; set; }

    /// <summary>Packed and ready to ship. Always 0 on the brand-less row.</summary>
    public decimal PackedOnHand { get; set; }

    /// <summary>
    /// Total physical stock = <see cref="RawOnHand"/> + <see cref="PackedOnHand"/>. Derived rather
    /// than stored so the split and the total can never drift apart.
    /// </summary>
    public decimal OnHand => RawOnHand + PackedOnHand;

    /// <summary>Held for booked orders against <em>this</em> row.</summary>
    public decimal Reserved { get; set; }

    /// <summary>
    /// Available = OnHand - Reserved. Negative signals a shortfall (not an error). Not the number to
    /// report to a user on its own — see <see cref="StockBalance.Available"/> for why.
    /// </summary>
    public decimal Available => OnHand - Reserved;
}
