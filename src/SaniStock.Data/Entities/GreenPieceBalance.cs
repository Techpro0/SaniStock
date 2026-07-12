namespace SaniStock.Data.Entities;

/// <summary>Cached on-hand balance of green (unfired) ware for an item+colour.</summary>
public class GreenPieceBalance
{
    public int Id { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int ColourId { get; set; }
    public Colour? Colour { get; set; }

    public decimal OnHand { get; set; }
}
