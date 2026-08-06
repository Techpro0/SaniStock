using ClosedXML.Excel;
using SaniStock.Domain.Models;

namespace SaniStock.Reports;

/// <summary>Excel exports whose layout mirrors their PDF counterpart.</summary>
public static class ExcelReports
{
    /// <summary>
    /// Orders export laid out as one section per order (mirrors <see cref="OrdersReportDocument"/>):
    /// an order-info block (Order No, Date, Customer, Status) shown once, the column titles, then
    /// that order's lines, with a blank row separating one order from the next — all on one sheet.
    /// Order and line ordering is preserved exactly as supplied by the caller.
    /// </summary>
    public static void SaveOrders(IReadOnlyList<OrderReportRow> rows, string path, string sheetName = "Orders")
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(Trim(sheetName));

        if (rows.Count == 0)
        {
            ws.Cell(1, 1).Value = "No orders in the selected date range.";
            wb.SaveAs(path);
            return;
        }

        // Group by Order No, preserving group order and within-group line order.
        var orderNos = new List<string>();
        var byOrder = new Dictionary<string, List<OrderReportRow>>();
        foreach (var r in rows)
        {
            if (!byOrder.TryGetValue(r.OrderNo, out var list))
            {
                list = new List<OrderReportRow>();
                byOrder[r.OrderNo] = list;
                orderNos.Add(r.OrderNo);
            }
            list.Add(r);
        }

        string[] titles = { "Item / Accessory", "Grade", "Colour", "Brand", "Ordered", "Sent", "Left to Send" };
        var row = 1;

        foreach (var no in orderNos)
        {
            var lines = byOrder[no];
            var head = lines[0];

            // Order-info block (two rows, kept in the first four columns).
            Label(ws, row, 1, "Order No:"); ws.Cell(row, 2).Value = no; ws.Cell(row, 2).Style.Font.Bold = true;
            Label(ws, row, 3, "Date:"); ws.Cell(row, 4).Value = head.OrderDate; ws.Cell(row, 4).Style.DateFormat.Format = "dd-MMM-yyyy";
            row++;
            Label(ws, row, 1, "Customer:"); ws.Cell(row, 2).Value = head.Party;
            Label(ws, row, 3, "Status:"); ws.Cell(row, 4).Value = head.Status;
            row++;

            // Column titles.
            for (var c = 0; c < titles.Length; c++)
            {
                var cell = ws.Cell(row, c + 1);
                cell.Value = titles[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }
            row++;

            // Lines.
            foreach (var r in lines)
            {
                ws.Cell(row, 1).Value = r.ItemOrAccessory;
                ws.Cell(row, 2).Value = r.Grade;
                ws.Cell(row, 3).Value = r.Colour;
                ws.Cell(row, 4).Value = r.Brand;
                ws.Cell(row, 5).Value = r.Ordered;
                ws.Cell(row, 6).Value = r.Dispatched;
                ws.Cell(row, 7).Value = r.Pending;
                row++;
            }

            row++;   // blank separator row between orders
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    /// <summary>
    /// Finished-goods stock export, laid out to match the on-screen grid and the PDF: one row per
    /// item+grade+colour, with a packed column per brand between "Not Packed" and the totals.
    /// <para>
    /// Written by hand rather than through the generic reflection exporter, which walks an object's
    /// simple properties and would silently skip the per-brand breakdown — the whole point of this
    /// sheet — while emitting nothing in its place.
    /// </para>
    /// </summary>
    public static void SaveFinishedStock(FinishedStockView view, string path, string sheetName = "Finished Stock")
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(Trim(sheetName));

        var titles = new List<string> { "Item Code", "Item", "Grade", "Colour", "Not Packed" };
        titles.AddRange(view.Brands.Select(b => $"Packed — {b.Name}"));
        titles.AddRange(new[] { "Packed Total", "In Stock", "Booked", "Free" });

        for (var c = 0; c < titles.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = titles[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        var row = 2;
        foreach (var r in view.Rows)
        {
            var col = 1;
            ws.Cell(row, col++).Value = r.ItemCode;
            ws.Cell(row, col++).Value = r.ItemName;
            ws.Cell(row, col++).Value = r.Grade;
            ws.Cell(row, col++).Value = r.Colour;
            ws.Cell(row, col++).Value = r.RawOnHand;

            // Positional, matching the header: the breakdown is aligned to view.Brands.
            for (var i = 0; i < view.Brands.Count; i++)
                ws.Cell(row, col++).Value = i < r.PackedByBrand.Count ? r.PackedByBrand[i].Packed : 0m;

            ws.Cell(row, col++).Value = r.PackedOnHand;
            ws.Cell(row, col++).Value = r.OnHand;
            ws.Cell(row, col++).Value = r.Reserved;

            var free = ws.Cell(row, col);
            free.Value = r.Available;
            // Same signal the grid paints: booked beyond what exists is the shortfall, not an error.
            if (r.Available < 0) free.Style.Font.FontColor = XLColor.Red;

            row++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    /// <summary>
    /// Accessory stock export, laid out to match the on-screen grid and the PDF. Mirrors
    /// <see cref="SaveFinishedStock"/> exactly, minus item/grade/colour — written by hand for the
    /// same reason: the generic reflection exporter would drop the per-brand breakdown.
    /// </summary>
    public static void SaveAccessoryStock(AccessoryStockView view, string path, string sheetName = "Accessory Stock")
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(Trim(sheetName));

        var titles = new List<string> { "Code", "Accessory", "Not Packed" };
        titles.AddRange(view.Brands.Select(b => $"Packed — {b.Name}"));
        titles.AddRange(new[] { "Packed Total", "In Stock", "Booked", "Free" });

        for (var c = 0; c < titles.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = titles[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        var row = 2;
        foreach (var r in view.Rows)
        {
            var col = 1;
            ws.Cell(row, col++).Value = r.AccessoryCode;
            ws.Cell(row, col++).Value = r.AccessoryName;
            ws.Cell(row, col++).Value = r.RawOnHand;

            for (var i = 0; i < view.Brands.Count; i++)
                ws.Cell(row, col++).Value = i < r.PackedByBrand.Count ? r.PackedByBrand[i].Packed : 0m;

            ws.Cell(row, col++).Value = r.PackedOnHand;
            ws.Cell(row, col++).Value = r.OnHand;
            ws.Cell(row, col++).Value = r.Reserved;

            var free = ws.Cell(row, col);
            free.Value = r.Available;
            if (r.Available < 0) free.Style.Font.FontColor = XLColor.Red;

            row++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    private static void Label(IXLWorksheet ws, int row, int col, string text)
    {
        var c = ws.Cell(row, col);
        c.Value = text;
        c.Style.Font.Bold = true;
    }

    private static string Trim(string s) => s.Length > 31 ? s[..31] : s;
}
