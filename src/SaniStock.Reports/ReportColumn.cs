namespace SaniStock.Reports;

/// <summary>Declarative column definition for a tabular PDF report.</summary>
/// <typeparam name="T">Row type.</typeparam>
public sealed class ReportColumn<T>
{
    public string Header { get; }
    public float RelativeWidth { get; }
    public bool AlignRight { get; }
    public Func<T, string> Cell { get; }

    public ReportColumn(string header, float relativeWidth, Func<T, string> cell, bool alignRight = false)
    {
        Header = header;
        RelativeWidth = relativeWidth;
        Cell = cell;
        AlignRight = alignRight;
    }
}
