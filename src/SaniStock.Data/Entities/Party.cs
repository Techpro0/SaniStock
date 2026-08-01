namespace SaniStock.Data.Entities;

/// <summary>A customer / trading party that places orders.</summary>
public class Party : IActivatable
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Contact { get; set; }
    public string? Gstin { get; set; }
    public bool IsActive { get; set; } = true;
}
