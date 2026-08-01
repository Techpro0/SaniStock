using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SaniStock.Domain.Models;

namespace SaniStock.Reports;

/// <summary>
/// Orders report rendered as one <b>separate table per order</b>: the order-level info
/// (Order No, Date, Customer, Status) is shown once in a header block above each order's
/// table, instead of being repeated on every line of a single combined table.
/// Order and line ordering is preserved exactly as supplied by the caller
/// (newest order first; within an order, items then their accessories).
/// </summary>
public sealed class OrdersReportDocument : IDocument
{
    private readonly string _subtitle;
    private readonly IReadOnlyList<OrderGroup> _orders;

    private sealed record OrderGroup(
        string OrderNo, DateTime OrderDate, string Party, string Status,
        IReadOnlyList<OrderReportRow> Lines);

    public OrdersReportDocument(IReadOnlyList<OrderReportRow> rows, DateTime fromDate, DateTime toDate)
    {
        _subtitle = $"{fromDate:dd-MMM-yyyy} to {toDate:dd-MMM-yyyy}";
        _orders = GroupPreservingOrder(rows);
    }

    /// <summary>Groups rows by Order No while preserving both group order and within-group line order.</summary>
    private static IReadOnlyList<OrderGroup> GroupPreservingOrder(IReadOnlyList<OrderReportRow> rows)
    {
        var order = new List<OrderGroup>();
        var buckets = new Dictionary<string, List<OrderReportRow>>();
        foreach (var r in rows)
        {
            if (!buckets.TryGetValue(r.OrderNo, out var lines))
            {
                lines = new List<OrderReportRow>();
                buckets[r.OrderNo] = lines;
                order.Add(new OrderGroup(r.OrderNo, r.OrderDate, r.Party, r.Status, lines));
            }
            lines.Add(r);
        }
        return order;
    }

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(28);
            page.Size(PageSizes.A4);
            page.DefaultTextStyle(t => t.FontSize(9));

            page.Header().Element(ReportLayout.Header("Orders Report", _subtitle));
            page.Content().PaddingVertical(8).Element(BuildBody);
            page.Footer().Element(ReportLayout.Footer);
        });
    }

    private void BuildBody(IContainer container)
    {
        if (_orders.Count == 0)
        {
            container.Text("No orders in the selected date range.").FontColor(Colors.Grey.Darken1);
            return;
        }

        container.Column(col =>
        {
            col.Spacing(16);   // visual separation between one order block and the next
            foreach (var order in _orders)
                col.Item().Element(c => RenderOrder(c, order));
        });
    }

    private static void RenderOrder(IContainer container, OrderGroup order)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(4f);    // Item / Accessory
                cols.RelativeColumn(1.5f);  // Grade
                cols.RelativeColumn(1.5f);  // Colour
                cols.RelativeColumn(2f);    // Brand
                cols.RelativeColumn(1.5f);  // Ordered
                cols.RelativeColumn(1.5f);  // Sent
                cols.RelativeColumn(1.5f);  // Left to Send
            });

            // The order-level info block plus the column titles are the table's HEADER, so they
            // stay glued to the lines: the header is never orphaned at a page bottom, and if an
            // order's lines run past a page it repeats atop the continuation. This is the
            // "keep header + table together" behaviour without a fixed keep-together that would
            // overflow for an order taller than one page.
            table.Header(header =>
            {
                header.Cell().ColumnSpan(7).Element(c => RenderOrderHeaderBlock(c, order));

                void H(string text, bool right = false)
                {
                    var cell = ReportLayout.HeaderCell(header.Cell());
                    cell = right ? cell.AlignRight() : cell.AlignLeft();
                    cell.Text(text).SemiBold();
                }
                H("Item / Accessory");
                H("Grade");
                H("Colour");
                H("Brand");
                H("Ordered", true);
                H("Sent", true);
                H("Left to Send", true);
            });

            var i = 0;
            foreach (var r in order.Lines)
            {
                var background = i++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;

                void C(string text, bool right = false)
                {
                    var cell = ReportLayout.BodyCell(table.Cell().Background(background));
                    cell = right ? cell.AlignRight() : cell.AlignLeft();
                    cell.Text(text);
                }
                C(r.ItemOrAccessory);
                C(r.Grade);
                C(r.Colour);
                C(r.Brand);
                C(r.Ordered.ToString("0.###"), true);
                C(r.Dispatched.ToString("0.###"), true);
                C(r.Pending.ToString("0.###"), true);
            }
        });
    }

    /// <summary>The order-level info shown once above each order's lines (Order No, Date, Customer, Status).</summary>
    private static void RenderOrderHeaderBlock(IContainer container, OrderGroup order)
    {
        container
            .Background(Colors.Grey.Lighten3)
            .BorderBottom(1).BorderColor(Colors.Grey.Medium)
            .PaddingVertical(5).PaddingHorizontal(6)
            .Row(row =>
            {
                void Field(float size, string label, string value) =>
                    row.RelativeItem(size).Text(t =>
                    {
                        t.Span($"{label}: ").SemiBold().FontColor(Colors.Grey.Darken3);
                        t.Span(value);
                    });

                Field(2.5f, "Order No", order.OrderNo);
                Field(2f, "Date", order.OrderDate.ToString("dd-MMM-yyyy"));
                Field(3.5f, "Customer", order.Party);
                Field(2f, "Status", order.Status);
            });
    }
}
