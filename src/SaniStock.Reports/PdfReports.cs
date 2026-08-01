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

    /// <summary>
    /// Finished-goods stock: one row per item+grade+colour, with a packed column per brand between
    /// "Not Packed" and the totals. Column positions follow <see cref="FinishedStockView.Brands"/>,
    /// which every row's breakdown is aligned to, so the layout matches the on-screen grid exactly.
    /// <para>
    /// The page turns landscape once the brand list makes portrait too tight. Past roughly eight
    /// brands even landscape stops being readable — at that point the report wants a rollup rather
    /// than more columns.
    /// </para>
    /// </summary>
    public static void SaveFinishedStock(FinishedStockView view, string path, DateTime asOf)
    {
        var cols = new List<ReportColumn<StockRow>>
        {
            new("Item Code", 2, r => r.ItemCode),
            new("Item", 4, r => r.ItemName),
            new("Grade", 1.5f, r => r.Grade),
            new("Colour", 2, r => r.Colour),
            new("Not Packed", 2, r => r.RawOnHand.ToString("0.###"), alignRight: true),
        };

        // Capture the index, not the loop variable: the closure has to read the same slot of every
        // row's aligned breakdown.
        for (var i = 0; i < view.Brands.Count; i++)
        {
            var slot = i;
            cols.Add(new ReportColumn<StockRow>(
                $"Packed\n{view.Brands[slot].Name}", 2,
                r => slot < r.PackedByBrand.Count ? r.PackedByBrand[slot].Packed.ToString("0.###") : "0",
                alignRight: true));
        }

        cols.Add(new ReportColumn<StockRow>("Packed Total", 2, r => r.PackedOnHand.ToString("0.###"), alignRight: true));
        cols.Add(new ReportColumn<StockRow>("In Stock", 2, r => r.OnHand.ToString("0.###"), alignRight: true));
        cols.Add(new ReportColumn<StockRow>("Booked", 2, r => r.Reserved.ToString("0.###"), alignRight: true));
        cols.Add(new ReportColumn<StockRow>("Free", 2, r => r.Available.ToString("0.###"), alignRight: true));

        new TableReportDocument<StockRow>("Finished Goods Stock",
            $"As of {asOf:dd-MMM-yyyy}", cols, view.Rows,
            landscape: view.Brands.Count > 3).GeneratePdf(path);
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

    /// <summary>
    /// Orders report — one separate table per order, with the order-level info (Order No, Date,
    /// Customer, Status) shown once in a header block above each order's lines.
    /// </summary>
    public static void SaveOrders(IReadOnlyList<OrderReportRow> rows, string path,
        DateTime fromDate, DateTime toDate)
        => new OrdersReportDocument(rows, fromDate, toDate).GeneratePdf(path);
}
