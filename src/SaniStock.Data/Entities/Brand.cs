namespace SaniStock.Data.Entities;

/// <summary>
/// A brand the factory packs ware under. The same physical piece — item, grade, colour — comes out
/// of the kiln undifferentiated and only becomes "Brand A" or "Brand B" when someone decides that
/// at packing time. Brand therefore exists from packing onward only: unpacked stock is brand-less
/// and shared, packed stock belongs to exactly one brand, and orders are placed against a brand.
/// </summary>
public class Brand
{
    /// <summary>The code seeded on every database so packing works out of the box.</summary>
    public const string DefaultCode = "UNBRANDED";

    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
