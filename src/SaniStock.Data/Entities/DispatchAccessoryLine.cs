namespace SaniStock.Data.Entities;

/// <summary>A quantity of one order accessory line shipped on a dispatch note.</summary>
public class DispatchAccessoryLine
{
    public int Id { get; set; }

    public int DispatchEntryId { get; set; }
    public DispatchEntry? DispatchEntry { get; set; }

    public int OrderAccessoryLineId { get; set; }
    public OrderAccessoryLine? OrderAccessoryLine { get; set; }

    public decimal Quantity { get; set; }
}
