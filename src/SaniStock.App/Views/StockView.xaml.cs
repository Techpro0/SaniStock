using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SaniStock.App.ViewModels;

namespace SaniStock.App.Views;

public partial class StockView : UserControl
{
    /// <summary>
    /// Where the generated brand columns go: straight after Item / Grade / Colour / Not Packed,
    /// and before the Packed Total / In Stock / Booked / Free block. Keeping the per-brand detail
    /// next to the shared unpacked number is what makes the row readable — "not packed yet" then
    /// "packed, by brand" then the totals.
    /// </summary>
    private const int BrandColumnStart = 4;

    /// <summary>Same idea for the Accessories grid: after Accessory / Not Packed.</summary>
    private const int AccessoryBrandColumnStart = 2;

    private StockViewModel? _boundViewModel;

    public StockView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_boundViewModel is not null)
            _boundViewModel.BrandColumnsChanged -= OnBrandColumnsChanged;

        _boundViewModel = DataContext as StockViewModel;

        if (_boundViewModel is not null)
        {
            _boundViewModel.BrandColumnsChanged += OnBrandColumnsChanged;
            RebuildBrandColumns();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // The shell resolves a fresh view model per navigation; without this the old one keeps the
        // discarded view alive through the event handler.
        if (_boundViewModel is not null)
            _boundViewModel.BrandColumnsChanged -= OnBrandColumnsChanged;
        _boundViewModel = null;
    }

    private void OnBrandColumnsChanged(object? sender, EventArgs e) => RebuildBrandColumns();

    /// <summary>
    /// Replaces the generated columns with one per brand. Rebuilding wholesale rather than diffing
    /// keeps this honest when a brand is added, renamed, deactivated or reordered — the list is
    /// short and this runs once per refresh.
    /// </summary>
    private void RebuildBrandColumns()
    {
        if (_boundViewModel is null) return;

        SpliceBrandColumns(FinishedGrid, BrandColumnStart);
        // Both grids are index-aligned to the same BrandColumns list (GetStockBrandColumns()
        // considers finished and accessory packed stock together), so one rebuild loop covers both.
        SpliceBrandColumns(AccessoryGrid, AccessoryBrandColumnStart);
    }

    private void SpliceBrandColumns(DataGrid grid, int insertAt)
    {
        if (_boundViewModel is null) return;

        foreach (var stale in grid.Columns.Where(IsGenerated).ToList())
            grid.Columns.Remove(stale);

        var at = insertAt;
        for (var i = 0; i < _boundViewModel.BrandColumns.Count; i++)
        {
            var brand = _boundViewModel.BrandColumns[i];
            var column = new DataGridTextColumn
            {
                Header = brand.Name,
                Width = 100,
                // Positional: every row's PackedByBrand is built index-aligned to BrandColumns, so
                // slot i is this brand on every row — no per-row lookup needed.
                Binding = new Binding($"PackedByBrand[{i}].Packed") { StringFormat = "{0:0.###}" }
            };
            MarkGenerated(column);
            grid.Columns.Insert(at++, column);
        }
    }

    // WPF columns carry no tag of their own, so generated ones are marked with an attached
    // dependency property and found again by it on the next rebuild.
    private static readonly DependencyProperty IsBrandColumnProperty =
        DependencyProperty.RegisterAttached("IsBrandColumn", typeof(bool), typeof(StockView),
            new PropertyMetadata(false));

    private static void MarkGenerated(DataGridColumn column) =>
        column.SetValue(IsBrandColumnProperty, true);

    private static bool IsGenerated(DataGridColumn column) =>
        (bool)column.GetValue(IsBrandColumnProperty);
}
