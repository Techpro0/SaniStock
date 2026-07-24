using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SaniStock.Data;
using SaniStock.Domain.Services;
using SaniStock.Reports;

// Renders a sample Orders Report from the app's real database, for reviewing the layout.
// Writes both a PDF and one PNG per page next to it.
// Usage: dotnet run --project tools/SaniStock.ReportSample -- "<output.pdf>" ["<db path>"]

PdfReports.EnsureLicense();

var outPath = args.Length > 0 ? args[0] : "orders-sample.pdf";
var dbPath = args.Length > 1
    ? args[1]
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SaniStock", "sanistock.db");

var options = new DbContextOptionsBuilder<SaniStockDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;

using var db = new SaniStockDbContext(options);
var reports = new ReportService(db);

var to = DateTime.Today;
var from = to.AddMonths(-3);
var rows = reports.GetOrders(from, to);

var doc = new OrdersReportDocument(rows, from, to);
doc.GeneratePdf(outPath);

var dir = Path.GetDirectoryName(outPath) ?? ".";
var stem = Path.GetFileNameWithoutExtension(outPath);
doc.GenerateImages(
    i => Path.Combine(dir, $"{stem}-p{i + 1}.png"),
    new ImageGenerationSettings { RasterDpi = 140, ImageFormat = ImageFormat.Png });

Console.WriteLine($"Wrote {rows.Count} order lines across {rows.Select(r => r.OrderNo).Distinct().Count()} orders.");
Console.WriteLine($"PDF: {outPath}");
Console.WriteLine($"PNGs: {stem}-p*.png in {dir}");

// Also generate the Orders Excel (per-order sections) and dump its first rows to verify layout.
var xlsxPath = Path.Combine(dir, $"{stem}.xlsx");
ExcelReports.SaveOrders(rows, xlsxPath);
Console.WriteLine($"XLSX: {xlsxPath}");
Console.WriteLine("--- first 16 rows of the Orders sheet ---");
using (var wb = new ClosedXML.Excel.XLWorkbook(xlsxPath))
{
    var ws = wb.Worksheet(1);
    var used = ws.RangeUsed();
    int maxRow = Math.Min(16, used?.RowCount() ?? 0);
    int maxCol = used?.ColumnCount() ?? 0;
    for (int r = 1; r <= maxRow; r++)
    {
        var cells = Enumerable.Range(1, maxCol).Select(c => ws.Cell(r, c).GetFormattedString());
        Console.WriteLine($"  {r,2}| " + string.Join(" | ", cells));
    }
}
