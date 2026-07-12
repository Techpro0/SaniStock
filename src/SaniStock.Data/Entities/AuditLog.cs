namespace SaniStock.Data.Entities;

/// <summary>
/// One row per stock-affecting action (and other notable events): who did what,
/// when, and the before/after quantity where applicable.
/// </summary>
public class AuditLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    /// <summary>Human-readable summary, e.g. "OnHand 10 -> 8".</summary>
    public string Details { get; set; } = string.Empty;
    public decimal? QuantityBefore { get; set; }
    public decimal? QuantityAfter { get; set; }
}
