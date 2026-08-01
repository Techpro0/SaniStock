namespace SaniStock.Domain.Models;

/// <summary>Input to post a production entry.</summary>
public record ProductionInput(DateTime Date, int ItemId, int GradeId, int ColourId, decimal Quantity, string? Remarks);

/// <summary>How much of a packing action goes to one brand.</summary>
public record PackingBrandLine(int BrandId, decimal Quantity);

/// <summary>
/// Input to pack already-produced ware, moving it from the shared unpacked pool into packed stock.
/// Packing is what assigns brand, and one action can be split across several brands at once — the
/// quantities in <see cref="Lines"/> are validated together against what is unpacked.
/// </summary>
public record PackingInput(DateTime Date, int ItemId, int GradeId, int ColourId,
    IReadOnlyList<PackingBrandLine> Lines, string? Remarks);

/// <summary>
/// One finished-ware line requested when booking an order. The item's active accessory
/// defaults are auto-reserved alongside it, except any accessory whose id appears in
/// <see cref="ExcludedAccessoryIds"/> (the per-line include/exclude choice).
/// <see cref="BrandId"/> is required: orders are always placed against a brand, even when the
/// stock that ends up covering them is still in the brand-less unpacked pool.
/// </summary>
public record OrderLineInput(int ItemId, int GradeId, int ColourId, int BrandId, decimal Quantity,
    IReadOnlyList<int>? ExcludedAccessoryIds = null);

/// <summary>One manually-added standalone accessory line requested when booking an order.</summary>
public record OrderAccessoryLineInput(int AccessoryId, decimal Quantity);

/// <summary>Input to book a new order.</summary>
public record OrderInput(
    int PartyId,
    DateTime OrderDate,
    string? Remarks,
    IReadOnlyList<OrderLineInput> Lines,
    IReadOnlyList<OrderAccessoryLineInput> AccessoryLines);

/// <summary>A quantity of a specific order line to ship.</summary>
public record DispatchLineInput(int OrderLineId, decimal Quantity);

/// <summary>A quantity of a specific order accessory line to ship.</summary>
public record DispatchAccessoryLineInput(int OrderAccessoryLineId, decimal Quantity);

/// <summary>Input to post a dispatch against an order.</summary>
public record DispatchInput(
    int OrderId,
    DateTime Date,
    string? Remarks,
    IReadOnlyList<DispatchLineInput> Lines,
    IReadOnlyList<DispatchAccessoryLineInput> AccessoryLines);

/// <summary>Input to record accessory stock received.</summary>
public record AccessoryReceiptInput(DateTime Date, int AccessoryId, decimal Quantity, string? Remarks);

/// <summary>Input to record a green-ware (unfired) in/out movement.</summary>
public record GreenPieceInput(DateTime Date, int ItemId, int ColourId, bool IsIssue, decimal Quantity, string? Remarks);

/// <summary>Input to record a raw-material in/out movement.</summary>
public record RawMaterialInput(DateTime Date, int RawMaterialId, bool IsIssue, decimal Quantity, string? Remarks);
