namespace SaniStock.Data.Entities;

/// <summary>
/// Records which physical stock a booked order line drew its reservation from. A 1st-grade line
/// may be covered from several sources — packed then unpacked 1st grade, then packed then unpacked
/// 2nd grade — and this is the traceable record of that split, in the order it was drawn.
/// <para>
/// <see cref="GradeId"/> and <see cref="BrandId"/> together are authoritative: they identify the
/// <see cref="StockBalance"/> row whose <see cref="StockBalance.Reserved"/> was increased, and
/// therefore the row that dispatch must deduct from and cancellation must release.
/// <see cref="BrandId"/> is null when the draw came from the shared unpacked pool, which has no
/// brand — any order can draw against it regardless of the brand it was placed for.
/// <see cref="Bucket"/> is the booking-time snapshot of the sub-state and is advisory only,
/// because packing may move the quantity between buckets before it ships.
/// </para>
/// <para>
/// Brand is advisory in the same limited sense: dispatch prefers the recorded source but falls back
/// to its sibling — a brand-less draw to that brand's packed stock, a branded draw to the unpacked
/// pool — because packing (and un-packing) legitimately moves quantity between the two after
/// booking. It never falls back to a <em>different</em> brand's packed stock: those pieces are
/// physically in the wrong boxes.
/// </para>
/// Unlike the stock ledger this row is not immutable — it tracks how much of the allocation has
/// since been shipped or released, exactly as <see cref="OrderLine"/> does for the line as a whole.
/// </summary>
public class OrderLineAllocation
{
    public int Id { get; set; }

    public int OrderLineId { get; set; }
    public OrderLine? OrderLine { get; set; }

    /// <summary>The grade the quantity was actually drawn from — not necessarily the line's grade.</summary>
    public int GradeId { get; set; }
    public Grade? Grade { get; set; }

    /// <summary>
    /// The brand whose packed stock was drawn, or null for the shared (brand-less) unpacked pool.
    /// A shortfall draw is also null: it has no physical source, and producing more lands in the
    /// unpacked pool.
    /// </summary>
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public StockBucket Bucket { get; set; }

    /// <summary>
    /// Position in the draw order (0 = first source tried). Dispatch consumes ascending;
    /// cancellation releases descending, so the last-borrowed source is the first given back.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>Quantity allocated from this source when the order was booked.</summary>
    public decimal Quantity { get; set; }

    public decimal QuantityDispatched { get; set; }
    public decimal QuantityReleased { get; set; }

    /// <summary>Still held against this source.</summary>
    public decimal QuantityReserved => Quantity - QuantityDispatched - QuantityReleased;
}
