using System.Collections;
using System.Reflection;
using ClosedXML.Excel;

namespace SaniStock.App.Infrastructure;

/// <summary>Exports any list of objects to an .xlsx file, one column per public property.</summary>
public static class ExcelExporter
{
    public static void Save(IEnumerable rows, string path, string sheetName = "Data")
    {
        var items = rows.Cast<object>().ToList();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(Trim(sheetName));

        if (items.Count == 0)
        {
            ws.Cell(1, 1).Value = "No data";
            wb.SaveAs(path);
            return;
        }

        var props = items[0].GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && IsSimple(p.PropertyType))
            .ToList();

        for (var c = 0; c < props.Count; c++)
        {
            ws.Cell(1, c + 1).Value = Humanize(props[c].Name);
            ws.Cell(1, c + 1).Style.Font.Bold = true;
        }

        for (var r = 0; r < items.Count; r++)
        {
            for (var c = 0; c < props.Count; c++)
            {
                var value = props[c].GetValue(items[r]);
                ws.Cell(r + 2, c + 1).Value = value switch
                {
                    null => XLCellValue.FromObject(null),
                    DateTime dt => dt,
                    bool bl => bl,
                    decimal dec => dec,
                    int i => i,
                    double d => d,
                    _ => value.ToString()
                };
            }
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
        wb.SaveAs(path);
    }

    private static bool IsSimple(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum || t == typeof(string)
               || t == typeof(decimal) || t == typeof(DateTime);
    }

    private static string Humanize(string name) =>
        string.Concat(name.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + ch : ch.ToString()));

    private static string Trim(string s) => s.Length > 31 ? s[..31] : s;
}
