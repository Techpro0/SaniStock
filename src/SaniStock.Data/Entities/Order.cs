namespace SaniStock.Data.Entities;

/// <summary>A customer order. Booking reserves stock but never changes on-hand.</summary>
public class Order
{
    public int Id { get; set; }
    public string OrderNo { get; set; } = string.Empty;

    public int PartyId { get; set; }
    public Party? Party { get; set; }

    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Booked;
    public string? Remarks { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public List<OrderLine> Lines { get; set; } = new();
    public List<OrderAccessoryLine> AccessoryLines { get; set; } = new();
}
