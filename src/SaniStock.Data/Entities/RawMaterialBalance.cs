namespace SaniStock.Data.Entities;

/// <summary>Cached on-hand balance of a raw material.</summary>
public class RawMaterialBalance
{
    public int Id { get; set; }

    public int RawMaterialId { get; set; }
    public RawMaterial? RawMaterial { get; set; }

    public decimal OnHand { get; set; }
}
