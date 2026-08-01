namespace SaniStock.Data.Entities;

/// <summary>A finished-ware line on an order (item+grade+colour+brand).</summary>
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

    /// <summary>
    /// The brand the customer ordered. Required: an order is always for branded goods, even though
    /// the stock covering it may still be sitting in the brand-less unpacked pool.
    /// </summary>
    public int BrandId { get; set; }
    public Brand? Brand { get; set; }

    public decimal QuantityOrdered { get; set; }
    public decimal QuantityDispatched { get; set; }

    /// <summary>Reserved quantity still held (ordered - dispatched), unless the line was released.</summary>
    public decimal QuantityReserved { get; set; }

    public decimal QuantityPending => QuantityOrdered - QuantityDispatched;

    /// <summary>
    /// Which grade/bucket/brand sources this line's reservation was drawn from, in draw order.
    /// A 1st-grade line may borrow from 2nd grade, so these are not always this line's own grade;
    /// and a draw against the shared unpacked pool carries no brand at all.
    /// </summary>
    public List<OrderLineAllocation> Allocations { get; set; } = new();
}
