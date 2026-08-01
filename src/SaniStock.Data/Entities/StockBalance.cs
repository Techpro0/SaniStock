namespace SaniStock.Data.Entities;

/// <summary>
/// Cached on-hand/reserved balance for a finished-goods item+grade+colour+brand combination.
/// Maintained incrementally by the stock service and rebuildable from <see cref="StockMovement"/>.
/// On-hand is split into two sub-states — unpacked (<see cref="RawOnHand"/>) and packed
/// (<see cref="PackedOnHand"/>) — that only packing moves quantity between.
/// <para>
/// <b>Brand splits the row.</b> <see cref="BrandId"/> is null on the shared <em>unpacked</em> pool
/// for the item+grade+colour, and set on one row per brand holding that brand's <em>packed</em>
/// stock. The two never mix, which gives this invariant:
/// </para>
/// <list type="bullet">
///   <item>brand-less row (<see cref="BrandId"/> null): <see cref="PackedOnHand"/> is always 0</item>
///   <item>branded row (<see cref="BrandId"/> set): <see cref="RawOnHand"/> is always 0</item>
/// </list>
/// <para>
/// It is enforced in <c>StockService.ApplyFinished</c> rather than by a database check constraint:
/// balances are a cache, and a constraint would make <c>ReconcileAll()</c> throw at exactly the
/// moment you most need it to run and show you the damage. The invariant is really a property of
/// the movements, and <c>ApplyFinished</c> is the single place they are written.
/// </para>
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

    /// <summary>Null on the shared unpacked pool; set on a brand's packed stock.</summary>
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    /// <summary>Produced but not yet packed. Always 0 on a branded row.</summary>
    public decimal RawOnHand { get; set; }

    /// <summary>Packed and ready to ship. Always 0 on the brand-less row.</summary>
    public decimal PackedOnHand { get; set; }

    /// <summary>
    /// Total physical stock = <see cref="RawOnHand"/> + <see cref="PackedOnHand"/>. Derived rather
    /// than stored so the split and the total can never drift apart.
    /// </summary>
    public decimal OnHand => RawOnHand + PackedOnHand;

    /// <summary>
    /// Held for booked orders against <em>this</em> row. Because brand splits packed stock onto its
    /// own row, a reservation is recorded exactly where it was drawn from — the shared unpacked pool
    /// or one brand's packed stock — so no bucket-order heuristic is needed to work out what is free.
    /// </summary>
    public decimal Reserved { get; set; }

    /// <summary>
    /// Available = OnHand - Reserved. Negative signals a shortfall (not an error).
    /// <para>
    /// Meaningful per row, but <b>not</b> the number to report to a user: dispatch may draw a
    /// reservation recorded here from the sibling row (packed stock can cover an unpacked
    /// reservation and vice versa), so a single row can read negative while the item+grade+colour
    /// as a whole is fully covered. Shortfall reporting aggregates across brand rows.
    /// </para>
    /// </summary>
    public decimal Available => OnHand - Reserved;
}
