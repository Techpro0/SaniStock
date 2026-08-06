namespace SaniStock.Data.Entities;

/// <summary>An accessory line on an order.</summary>
public class OrderAccessoryLine
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int AccessoryId { get; set; }
    public Accessory? Accessory { get; set; }

    /// <summary>
    /// The item line this accessory was auto-attached to (from the item's accessory defaults).
    /// Null for a manually-added standalone accessory line.
    /// </summary>
    public int? SourceOrderLineId { get; set; }
    public OrderLine? SourceOrderLine { get; set; }

    /// <summary>
    /// The brand this accessory was ordered under. An auto-attached line always takes its parent
    /// item line's brand, so the reservation prefers that brand's packed accessory stock over the
    /// shared unpacked pool. A manually-added standalone line names its own brand the same way an
    /// item line does.
    /// </summary>
    public int BrandId { get; set; }
    public Brand? Brand { get; set; }

    public decimal QuantityOrdered { get; set; }
    public decimal QuantityDispatched { get; set; }
    public decimal QuantityReserved { get; set; }

    public decimal QuantityPending => QuantityOrdered - QuantityDispatched;

    /// <summary>
    /// Which brand/bucket sources this line's reservation was drawn from, in draw order. A draw
    /// against the shared unpacked pool carries no brand at all. Mirrors <see cref="OrderLine.Allocations"/>.
    /// </summary>
    public List<OrderAccessoryLineAllocation> Allocations { get; set; } = new();
}
