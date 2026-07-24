namespace SaniStock.Data.Entities;

/// <summary>A finished-ware product (e.g. a wash basin model).</summary>
public class Item
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string UnitOfMeasure { get; set; } = "PCS";
    public bool IsActive { get; set; } = true;

    /// <summary>Optional product category. Null on legacy items with no type assigned.</summary>
    public int? ProductTypeId { get; set; }
    public ProductType? ProductType { get; set; }
}
