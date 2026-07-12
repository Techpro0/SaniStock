using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SaniStock.Reports;

/// <summary>Shared header, footer and cell styling for all PDF reports.</summary>
public static class ReportLayout
{
    public const string CompanyName = "SaniStock — Sanitary Ware Works";
    public static readonly string BrandColor = Colors.Blue.Darken2;

    public static Action<IContainer> Header(string title, string? subtitle) => container =>
    {
        container.Column(col =>
        {
            col.Item().Text(CompanyName).FontSize(14).Bold().FontColor(BrandColor);
            col.Item().Text(title).FontSize(11).SemiBold();
            if (!string.IsNullOrWhiteSpace(subtitle))
                col.Item().Text(subtitle).FontSize(9).FontColor(Colors.Grey.Darken1);
            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(BrandColor);
        });
    };

    public static void Footer(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.Span("Printed ").FontColor(Colors.Grey.Medium);
                t.Span(DateTime.Now.ToString("dd-MMM-yyyy HH:mm")).FontColor(Colors.Grey.Medium);
            });
            row.RelativeItem().AlignRight().Text(t =>
            {
                t.Span("Page ").FontColor(Colors.Grey.Medium);
                t.CurrentPageNumber().FontColor(Colors.Grey.Medium);
                t.Span(" / ").FontColor(Colors.Grey.Medium);
                t.TotalPages().FontColor(Colors.Grey.Medium);
            });
        });
    }

    public static IContainer HeaderCell(IContainer c) =>
        c.Background(Colors.Grey.Lighten2).BorderBottom(1).BorderColor(Colors.Grey.Medium)
            .PaddingVertical(4).PaddingHorizontal(4);

    public static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).PaddingHorizontal(4);
}
