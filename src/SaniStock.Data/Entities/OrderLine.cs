namespace SaniStock.Data.Entities;

/// <summary>A finished-ware line on an order (item+grade+colour).</summary>
public class OrderLine
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int GradeId { get; set; }
    public Grade? Grade { get; set; }

    public int ColourId { get; set; }
    public Colour? Colour { get; set; }

    public decimal QuantityOrdered { get; set; }
    public decimal QuantityDispatched { get; set; }

    /// <summary>Reserved quantity still held (ordered - dispatched), unless the line was released.</summary>
    public decimal QuantityReserved { get; set; }

    public decimal QuantityPending => QuantityOrdered - QuantityDispatched;
}
