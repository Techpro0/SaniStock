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

// Finished stock, whose column count depends on how many brands exist. Worth previewing on real
// data: the brand columns are generated, the page flips to landscape once there are enough of
// them, and neither is visible from a unit test.
var stock = reports.GetFinishedStock();
var stockPdf = Path.Combine(dir, $"{stem}-stock.pdf");
var stockXlsx = Path.Combine(dir, $"{stem}-stock.xlsx");
PdfReports.SaveFinishedStock(stock, stockPdf, DateTime.Today);
ExcelReports.SaveFinishedStock(stock, stockXlsx);

Console.WriteLine();
Console.WriteLine($"Stock: {stock.Rows.Count} rows, {stock.Brands.Count} brand column(s): " +
                  string.Join(", ", stock.Brands.Select(b => b.Name)));
Console.WriteLine($"PDF: {stockPdf}");
Console.WriteLine($"XLSX: {stockXlsx}");
Console.WriteLine("--- first 8 rows of the Finished Stock sheet ---");
using (var wb = new ClosedXML.Excel.XLWorkbook(stockXlsx))
{
    var ws = wb.Worksheet(1);
    var used = ws.RangeUsed();
    int maxRow = Math.Min(8, used?.RowCount() ?? 0);
    int maxCol = used?.ColumnCount() ?? 0;
    for (int r = 1; r <= maxRow; r++)
    {
        var cells = Enumerable.Range(1, maxCol).Select(c => ws.Cell(r, c).GetFormattedString());
        Console.WriteLine($"  {r,2}| " + string.Join(" | ", cells));
    }
}

// Accessory stock — the other grid on the Stock screen, and the one its export buttons now follow
// when the Accessories tab is showing.
var accessoryView = reports.GetAccessoryStock();
var accPdf = Path.Combine(dir, $"{stem}-accessories.pdf");
PdfReports.SaveAccessoryStock(accessoryView, accPdf, DateTime.Today);

Console.WriteLine();
Console.WriteLine($"Accessory stock: {accessoryView.Rows.Count} rows");
Console.WriteLine($"PDF: {accPdf}");
foreach (var a in accessoryView.Rows.Take(8))
    Console.WriteLine($"  {a.AccessoryCode,-8} {a.AccessoryName,-22} " +
                      $"on hand {a.OnHand,8:0.###}  booked {a.Reserved,8:0.###}  free {a.Available,8:0.###}");
