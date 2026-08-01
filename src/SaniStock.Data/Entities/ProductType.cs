namespace SaniStock.Data.Entities;

/// <summary>
/// A category of finished ware (e.g. Water Closet, Wash Basin, Bib Cock). Sits above
/// <see cref="Item"/> in the setup hierarchy; every item may optionally belong to one.
/// </summary>
public class ProductType : IActivatable
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
