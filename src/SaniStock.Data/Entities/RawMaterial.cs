namespace SaniStock.Data.Entities;

/// <summary>A raw material consumed in production (optional module).</summary>
public class RawMaterial
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UnitOfMeasure { get; set; } = "KG";
    public bool IsActive { get; set; } = true;
}
