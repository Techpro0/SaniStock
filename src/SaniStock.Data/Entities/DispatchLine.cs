namespace SaniStock.Data.Entities;

/// <summary>A quantity of one order line shipped on a dispatch note.</summary>
public class DispatchLine
{
    public int Id { get; set; }

    public int DispatchEntryId { get; set; }
    public DispatchEntry? DispatchEntry { get; set; }

    public int OrderLineId { get; set; }
    public OrderLine? OrderLine { get; set; }

    public decimal Quantity { get; set; }
}
