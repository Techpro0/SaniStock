namespace SaniStock.Data.Entities;

/// <summary>A saleable accessory (e.g. fittings, seat covers) tracked without grade/colour.</summary>
public class Accessory
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string UnitOfMeasure { get; set; } = "PCS";
    public bool IsActive { get; set; } = true;
}
