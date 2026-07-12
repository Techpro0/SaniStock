using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SaniStock.Domain.Models;

namespace SaniStock.Reports;

/// <summary>
/// Facade that turns domain report rows into PDF files. Call <see cref="EnsureLicense"/> once
/// at startup (QuestPDF Community licence).
/// </summary>
public static class PdfReports
{
    public static void EnsureLicense() => QuestPDF.Settings.License = LicenseType.Community;

    public static void SaveFinishedStock(IReadOnlyList<StockRow> rows, string path, DateTime asOf)
    {
        var cols = new List<ReportColumn<StockRow>>
        {
            new("Item Code", 2, r => r.ItemCode),
            new("Item", 4, r => r.ItemName),
            new("Grade", 2, r => r.Grade),
            new("Colour", 2, r => r.Colour),
            new("In Stock", 2, r => r.OnHand.ToString("0.###"), alignRight: true),
            new("Booked", 2, r => r.Reserved.ToString("0.###"), alignRight: true),
            new("Free", 2, r => r.Available.ToString("0.###"), alignRight: true),
        };
        new TableReportDocument<StockRow>("Finished Goods Stock",
            $"As of {asOf:dd-MMM-yyyy}", cols, rows).GeneratePdf(path);
    }

    public static void SaveAccessoryStock(IReadOnlyList<AccessoryStockRow> rows, string path, DateTime asOf)
    {
        var cols = new List<ReportColumn<AccessoryStockRow>>
        {
            new("Code", 2, r => r.AccessoryCode),
            new("Accessory", 5, r => r.AccessoryName),
            new("In Stock", 2, r => r.OnHand.ToString("0.###"), alignRight: true),
            new("Booked", 2, r => r.Reserved.ToString("0.###"), alignRight: true),
            new("Free", 2, r => r.Available.ToString("0.###"), alignRight: true),
        };
        new TableReportDocument<AccessoryStockRow>("Accessory Stock",
            $"As of {asOf:dd-MMM-yyyy}", cols, rows).GeneratePdf(path);
    }

    public static void SaveShortfall(IReadOnlyList<ShortfallRow> rows, string path)
        => new ShortfallReportDocument(rows).GeneratePdf(path);

    public static void SaveProduction(IReadOnlyList<ProductionReportRow> rows, string path,
        DateTime fromDate, DateTime toDate)
    {
        var cols = new List<ReportColumn<ProductionReportRow>>
        {
            new("Date", 2, r => r.Date.ToString("dd-MMM-yyyy")),
            new("Item Code", 2, r => r.ItemCode),
            new("Item", 4, r => r.ItemName),
            new("Grade", 1.5f, r => r.Grade),
            new("Colour", 2, r => r.Colour),
            new("Qty", 2, r => (r.IsReversal ? "-" : "") + r.Quantity.ToString("0.###"), alignRight: true),
            new("By", 2, r => r.CreatedBy),
        };
        new TableReportDocument<ProductionReportRow>("Production Report",
            $"{fromDate:dd-MMM-yyyy} to {toDate:dd-MMM-yyyy}", cols, rows).GeneratePdf(path);
    }

    public static void SaveOrders(IReadOnlyList<OrderReportRow> rows, string path,
        DateTime fromDate, DateTime toDate)
    {
        var cols = new List<ReportColumn<OrderReportRow>>
        {
            new("Order No", 2, r => r.OrderNo),
            new("Date", 2, r => r.OrderDate.ToString("dd-MMM-yyyy")),
            new("Customer", 3, r => r.Party),
            new("Status", 2, r => r.Status),
            new("Item / Accessory", 3, r => r.ItemOrAccessory),
            new("Grade", 1.5f, r => r.Grade),
            new("Colour", 1.5f, r => r.Colour),
            new("Ordered", 1.5f, r => r.Ordered.ToString("0.###"), alignRight: true),
            new("Sent", 1.5f, r => r.Dispatched.ToString("0.###"), alignRight: true),
            new("Left", 1.5f, r => r.Pending.ToString("0.###"), alignRight: true),
        };
        new TableReportDocument<OrderReportRow>("Orders Report",
            $"{fromDate:dd-MMM-yyyy} to {toDate:dd-MMM-yyyy}", cols, rows).GeneratePdf(path);
    }
}
