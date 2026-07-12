namespace SaniStock.Data.Entities;

/// <summary>An accessory line on an order.</summary>
public class OrderAccessoryLine
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int AccessoryId { get; set; }
    public Accessory? Accessory { get; set; }

    public decimal QuantityOrdered { get; set; }
    public decimal QuantityDispatched { get; set; }
    public decimal QuantityReserved { get; set; }

    public decimal QuantityPending => QuantityOrdered - QuantityDispatched;
}
