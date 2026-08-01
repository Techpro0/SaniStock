namespace SaniStock.Data.Entities;

/// <summary>A ware colour / glaze. Hex code is optional (for UI swatches).</summary>
public class Colour : IActivatable
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? HexCode { get; set; }
    public bool IsActive { get; set; } = true;
}
