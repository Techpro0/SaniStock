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

        string[] titles = { "Item / Accessory", "Grade", "Colour", "Ordered", "Sent", "Left to Send" };
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
                ws.Cell(row, 4).Value = r.Ordered;
                ws.Cell(row, 5).Value = r.Dispatched;
                ws.Cell(row, 6).Value = r.Pending;
                row++;
            }

            row++;   // blank separator row between orders
        }

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
