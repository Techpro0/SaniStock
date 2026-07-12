namespace SaniStock.Domain.Models;

/// <summary>A row in the finished-goods stock view/report.</summary>
public record StockRow(
    int ItemId, string ItemCode, string ItemName,
    int GradeId, string Grade,
    int ColourId, string Colour,
    decimal OnHand, decimal Reserved)
{
    public decimal Available => OnHand - Reserved;
}

/// <summary>A row in the accessory stock view/report.</summary>
public record AccessoryStockRow(
    int AccessoryId, string AccessoryCode, string AccessoryName,
    decimal OnHand, decimal Reserved)
{
    public decimal Available => OnHand - Reserved;
}

/// <summary>An open order/party driving a shortfall for a given stock key.</summary>
public record ShortfallDriver(string OrderNo, string Party, DateTime OrderDate, decimal PendingQuantity);

/// <summary>A finished-goods combination where Reserved exceeds OnHand — needs production.</summary>
public record ShortfallRow(
    int ItemId, string ItemCode, string ItemName,
    string Grade, string Colour,
    decimal OnHand, decimal Reserved, decimal Shortfall,
    IReadOnlyList<ShortfallDriver> Drivers);

/// <summary>A production history row.</summary>
public record ProductionReportRow(
    DateTime Date, string ItemCode, string ItemName,
    string Grade, string Colour, decimal Quantity, bool IsReversal, string CreatedBy);

/// <summary>An order line as seen in the orders report.</summary>
public record OrderReportRow(
    string OrderNo, DateTime OrderDate, string Party, string Status,
    string ItemOrAccessory, string Grade, string Colour,
    decimal Ordered, decimal Dispatched, decimal Pending);

/// <summary>Dashboard summary counts for the landing screen.</summary>
public record DashboardSummary(
    decimal TodayProductionQty,
    int TodayDispatchCount,
    int ShortfallCount,
    int LowStockCount,
    int OpenOrderCount);
