using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SaniStock.Reports;

/// <summary>A generic paginated, tabular PDF report with a branded header and page footer.</summary>
public sealed class TableReportDocument<T> : IDocument
{
    private readonly string _title;
    private readonly string? _subtitle;
    private readonly IReadOnlyList<ReportColumn<T>> _columns;
    private readonly IReadOnlyList<T> _rows;
    private readonly bool _landscape;

    /// <param name="landscape">
    /// Turn the page sideways. Reports whose column count depends on master data — the stock
    /// report grows a column per brand — need the extra width once they pass a handful of columns,
    /// or the cells squeeze until the numbers wrap.
    /// </param>
    public TableReportDocument(string title, string? subtitle,
        IReadOnlyList<ReportColumn<T>> columns, IReadOnlyList<T> rows, bool landscape = false)
    {
        _title = title;
        _subtitle = subtitle;
        _columns = columns;
        _rows = rows;
        _landscape = landscape;
    }

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(28);
            page.Size(_landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.DefaultTextStyle(t => t.FontSize(9));

            page.Header().Element(ReportLayout.Header(_title, _subtitle));
            page.Content().PaddingVertical(8).Element(BuildTable);
            page.Footer().Element(ReportLayout.Footer);
        });
    }

    private void BuildTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                foreach (var c in _columns)
                    cols.RelativeColumn(c.RelativeWidth);
            });

            table.Header(header =>
            {
                foreach (var c in _columns)
                {
                    var cell = ReportLayout.HeaderCell(header.Cell());
                    cell = c.AlignRight ? cell.AlignRight() : cell.AlignLeft();
                    cell.Text(c.Header).SemiBold();
                }
            });

            var i = 0;
            foreach (var row in _rows)
            {
                var background = i++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                foreach (var c in _columns)
                {
                    var cell = ReportLayout.BodyCell(table.Cell().Background(background));
                    cell = c.AlignRight ? cell.AlignRight() : cell.AlignLeft();
                    cell.Text(c.Cell(row));
                }
            }
        });
    }
}
