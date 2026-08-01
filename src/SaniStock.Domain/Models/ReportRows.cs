namespace SaniStock.Domain.Models;

/// <summary>
/// One brand shown as a column on the finished-goods stock view. The set is chosen once per query
/// and every <see cref="StockRow.PackedByBrand"/> is index-aligned to it, so a grid, a PDF table
/// and a spreadsheet can all lay the columns out by position.
/// </summary>
public record BrandColumn(int BrandId, string Code, string Name);

/// <summary>How much of a stock row is packed under one brand. Zero when that brand holds none.</summary>
public record BrandPacked(int BrandId, string Brand, decimal Packed);

/// <summary>
/// The finished-goods stock view: the brand columns to show, and the rows to show them on.
/// One row per item+grade+colour — brand is extra detail across the row, never extra rows.
/// </summary>
public record FinishedStockView(IReadOnlyList<BrandColumn> Brands, IReadOnlyList<StockRow> Rows);

/// <summary>
/// A row in the finished-goods stock view/report: one item+grade+colour, with its packed quantity
/// broken out per brand. "Not packed" (<see cref="RawOnHand"/>) is a single shared number, because
/// unpacked ware has not been assigned a brand yet.
/// </summary>
public record StockRow(
    int ItemId, string ItemCode, string ItemName,
    int GradeId, string Grade,
    int ColourId, string Colour,
    decimal RawOnHand, decimal PackedOnHand, decimal Reserved,
    IReadOnlyList<BrandPacked> PackedByBrand)
{
    /// <summary>Total physical stock, packed or not, across every brand.</summary>
    public decimal OnHand => RawOnHand + PackedOnHand;

    public decimal Available => OnHand - Reserved;

    /// <summary>Packed quantity for one brand, 0 if that brand holds none of this combination.</summary>
    public decimal PackedFor(int brandId) =>
        PackedByBrand.FirstOrDefault(b => b.BrandId == brandId)?.Packed ?? 0m;
}

/// <summary>A row in the accessory stock view/report.</summary>
public record AccessoryStockRow(
    int AccessoryId, string AccessoryCode, string AccessoryName,
    decimal OnHand, decimal Reserved)
{
    public decimal Available => OnHand - Reserved;
}

/// <summary>
/// An open order/party driving a shortfall for a given stock key. <see cref="OrderedGrade"/> is the
/// grade the customer actually ordered, which differs from the grade being reported on when a
/// top-grade line was covered from the grade below. <see cref="Brand"/> is the brand the order was
/// placed for — context on the demand, not something you can produce for, since production lands in
/// the brand-less unpacked pool and only becomes branded at packing.
/// </summary>
public record ShortfallDriver(string OrderNo, string Party, DateTime OrderDate,
    decimal PendingQuantity, string OrderedGrade, string Brand);

/// <summary>A finished-goods combination where Reserved exceeds OnHand — needs production.</summary>
public record ShortfallRow(
    int ItemId, string ItemCode, string ItemName,
    string Grade, string Colour,
    decimal RawOnHand, decimal PackedOnHand, decimal Reserved, decimal Shortfall,
    IReadOnlyList<ShortfallDriver> Drivers)
{
    public decimal OnHand => RawOnHand + PackedOnHand;
}

/// <summary>A production history row.</summary>
public record ProductionReportRow(
    DateTime Date, string ItemCode, string ItemName,
    string Grade, string Colour, decimal Quantity, bool IsReversal, string CreatedBy);

/// <summary>
/// An order line as seen in the orders report. <see cref="Brand"/> is "-" on accessory lines,
/// which are tracked without grade, colour or brand.
/// </summary>
public record OrderReportRow(
    string OrderNo, DateTime OrderDate, string Party, string Status,
    string ItemOrAccessory, string Grade, string Colour, string Brand,
    decimal Ordered, decimal Dispatched, decimal Pending);

/// <summary>Dashboard summary counts for the landing screen.</summary>
public record DashboardSummary(
    decimal TodayProductionQty,
    int TodayDispatchCount,
    int ShortfallCount,
    int LowStockCount,
    int OpenOrderCount);
