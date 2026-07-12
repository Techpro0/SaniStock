namespace SaniStock.Data.Entities;

/// <summary>
/// Cached on-hand/reserved balance for a finished-goods item+grade+colour combination.
/// Maintained incrementally by the stock service and rebuildable from <see cref="StockMovement"/>.
/// </summary>
public class StockBalance
{
    public int Id { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public int GradeId { get; set; }
    public Grade? Grade { get; set; }

    public int ColourId { get; set; }
    public Colour? Colour { get; set; }

    public decimal OnHand { get; set; }
    public decimal Reserved { get; set; }

    /// <summary>Available = OnHand - Reserved. Negative signals a shortfall (not an error).</summary>
    public decimal Available => OnHand - Reserved;
}
