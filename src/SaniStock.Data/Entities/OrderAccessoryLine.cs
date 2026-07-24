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

    public decimal QuantityOrdered { get; set; }
    public decimal QuantityDispatched { get; set; }
    public decimal QuantityReserved { get; set; }

    public decimal QuantityPending => QuantityOrdered - QuantityDispatched;
}
