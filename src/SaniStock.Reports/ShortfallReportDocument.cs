using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SaniStock.Domain.Models;

namespace SaniStock.Reports;

/// <summary>
/// Production-planning report: each short combination followed by the open orders driving it.
/// </summary>
public sealed class ShortfallReportDocument : IDocument
{
    private readonly IReadOnlyList<ShortfallRow> _rows;

    public ShortfallReportDocument(IReadOnlyList<ShortfallRow> rows) => _rows = rows;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(28);
            page.Size(PageSizes.A4);
            page.DefaultTextStyle(t => t.FontSize(9));

            page.Header().Element(ReportLayout.Header(
                "What to Make",
                $"{_rows.Count} item(s) where you have booked more than you have in stock"));
            page.Content().PaddingVertical(8).Element(Body);
            page.Footer().Element(ReportLayout.Footer);
        });
    }

    private void Body(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(10);
            foreach (var r in _rows)
            {
                col.Item().Column(block =>
                {
                    block.Item().Background(Colors.Red.Lighten4).Padding(5).Row(row =>
                    {
                        row.RelativeItem(4).Text($"{r.ItemCode} — {r.ItemName}").SemiBold();
                        row.RelativeItem(2).Text($"Grade {r.Grade}");
                        row.RelativeItem(2).Text($"Colour {r.Colour}");
                        row.RelativeItem(2).AlignRight().Text($"In Stock {r.OnHand:0.###}");
                        row.RelativeItem(2).AlignRight().Text($"Booked {r.Reserved:0.###}");
                        row.RelativeItem(2).AlignRight().Text($"Make {r.Shortfall:0.###}")
                            .Bold().FontColor(Colors.Red.Darken2);
                    });

                    if (r.Drivers.Count > 0)
                    {
                        block.Item().PaddingLeft(12).PaddingTop(3).Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(3);
                                c.RelativeColumn(4);
                                c.RelativeColumn(2);
                                c.RelativeColumn(2);
                            });
                            t.Header(h =>
                            {
                                h.Cell().Element(ReportLayout.HeaderCell).Text("Order No").SemiBold();
                                h.Cell().Element(ReportLayout.HeaderCell).Text("Customer").SemiBold();
                                h.Cell().Element(ReportLayout.HeaderCell).AlignRight().Text("Order Date").SemiBold();
                                h.Cell().Element(ReportLayout.HeaderCell).AlignRight().Text("Waiting Qty").SemiBold();
                            });
                            foreach (var d in r.Drivers)
                            {
                                t.Cell().Element(ReportLayout.BodyCell).Text(d.OrderNo);
                                t.Cell().Element(ReportLayout.BodyCell).Text(d.Party);
                                t.Cell().Element(ReportLayout.BodyCell).AlignRight().Text(d.OrderDate.ToString("dd-MMM-yyyy"));
                                t.Cell().Element(ReportLayout.BodyCell).AlignRight().Text(d.PendingQuantity.ToString("0.###"));
                            }
                        });
                    }
                });
            }
        });
    }
}
