namespace SaniStock.Data.Entities;

/// <summary>A dispatch note against an order. Reduces on-hand and reserved for the lines shipped.</summary>
public class DispatchEntry
{
    public int Id { get; set; }
    public string DispatchNo { get; set; } = string.Empty;

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public DateTime Date { get; set; }
    public string DispatchedBy { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<DispatchLine> Lines { get; set; } = new();
    public List<DispatchAccessoryLine> AccessoryLines { get; set; } = new();
}
