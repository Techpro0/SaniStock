namespace SaniStock.Domain.Models;

/// <summary>Input to post a production entry.</summary>
public record ProductionInput(DateTime Date, int ItemId, int GradeId, int ColourId, decimal Quantity, string? Remarks);

/// <summary>One finished-ware line requested when booking an order.</summary>
public record OrderLineInput(int ItemId, int GradeId, int ColourId, decimal Quantity);

/// <summary>One accessory line requested when booking an order.</summary>
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
