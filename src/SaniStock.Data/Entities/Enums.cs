namespace SaniStock.Data.Entities;

/// <summary>Lifecycle status of a customer order.</summary>
public enum OrderStatus
{
    Booked = 0,
    PartiallyDispatched = 1,
    Dispatched = 2,
    Cancelled = 3
}

/// <summary>Role controlling menu visibility and permissions.</summary>
public enum UserRole
{
    Operator = 0,
    Admin = 1
}

/// <summary>
/// Classifies a signed entry in a stock ledger. Every stock-affecting action
/// writes an immutable movement; the cached balance is the running sum of these.
/// </summary>
public enum StockMovementType
{
    /// <summary>Production posted finished/green ware, or raw material received. Increases OnHand.</summary>
    Production = 0,
    /// <summary>Order booking reserved stock. Increases Reserved only.</summary>
    Reservation = 1,
    /// <summary>Order cancelled / line reduced. Decreases Reserved (releases the hold).</summary>
    ReservationRelease = 2,
    /// <summary>Goods physically dispatched against an order. Decreases OnHand and Reserved.</summary>
    Dispatch = 3,
    /// <summary>Manual correction or reversal of an earlier posting.</summary>
    Adjustment = 4,
    /// <summary>Raw material / green ware consumed or issued out. Decreases OnHand.</summary>
    Issue = 5,
    /// <summary>Finished ware packed: moves quantity from unpacked to packed. Total OnHand is unchanged.</summary>
    Packing = 6
}

/// <summary>
/// Which sub-state of finished on-hand stock a quantity sits in. Total OnHand is
/// always Raw + Packed; only <see cref="StockMovementType.Packing"/> moves quantity
/// between the two.
/// </summary>
public enum StockBucket
{
    /// <summary>Packed and ready to ship. Dispatch draws from here first.</summary>
    Packed = 0,
    /// <summary>Produced but not yet packed.</summary>
    Raw = 1,
    /// <summary>
    /// Not backed by physical stock at all — the part of a booking that ran the ordered
    /// grade's Available negative. Recorded so a line's allocations always sum to its
    /// reserved quantity; resolved against real stock when the goods are finally shipped.
    /// </summary>
    Shortfall = 2
}
