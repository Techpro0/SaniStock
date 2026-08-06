namespace SaniStock.Data.Entities;

/// <summary>
/// Records which physical accessory stock a booked order accessory line drew its reservation from.
/// Mirrors <see cref="OrderLineAllocation"/> for finished ware, minus grade — accessories aren't
/// graded, so a line draws at most two sources: this brand's packed stock, then the shared unpacked
/// pool.
/// <para>
/// <see cref="BrandId"/> is authoritative: it identifies the <see cref="AccessoryStockBalance"/> row
/// whose <see cref="AccessoryStockBalance.Reserved"/> was raised, and therefore the row that dispatch
/// must deduct from and cancellation must release. Null means the draw came from the shared unpacked
/// pool (or, for a <see cref="StockBucket.Shortfall"/> row, no physical source at all).
/// </para>
/// Unlike the ledger this row is not immutable — it tracks how much of the allocation has since been
/// shipped or released, exactly as <see cref="OrderLineAllocation"/> does.
/// </summary>
public class OrderAccessoryLineAllocation
{
    public int Id { get; set; }

    public int OrderAccessoryLineId { get; set; }
    public OrderAccessoryLine? OrderAccessoryLine { get; set; }

    /// <summary>The brand whose packed stock was drawn, or null for the shared unpacked pool / a shortfall.</summary>
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public StockBucket Bucket { get; set; }

    /// <summary>
    /// Position in the draw order (0 = first source tried). Dispatch consumes ascending;
    /// cancellation releases descending.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>Quantity allocated from this source when the line was booked.</summary>
    public decimal Quantity { get; set; }

    public decimal QuantityDispatched { get; set; }
    public decimal QuantityReleased { get; set; }

    /// <summary>Still held against this source.</summary>
    public decimal QuantityReserved => Quantity - QuantityDispatched - QuantityReleased;
}
