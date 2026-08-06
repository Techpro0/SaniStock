using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using SaniStock.App.Infrastructure;
using SaniStock.Domain.Models;
using SaniStock.Reports;

namespace SaniStock.App.ViewModels;

public partial class StockViewModel : ViewModelBase
{
    private readonly IDomainScopeFactory _scopes;
    private readonly IDialogService _dialogs;
    private List<StockRow> _allFinished = new();
    private List<AccessoryStockRow> _allAccessory = new();

    public StockViewModel(IDomainScopeFactory scopes, IDialogService dialogs)
    {
        _scopes = scopes;
        _dialogs = dialogs;
        Title = "Stock";
    }

    public ObservableCollection<StockRow> Finished { get; } = new();
    public ObservableCollection<AccessoryStockRow> Accessories { get; } = new();

    /// <summary>
    /// The brands to show a packed column for, in column order. Every row's
    /// <see cref="StockRow.PackedByBrand"/> is aligned to this list, so the grid, the PDF and the
    /// spreadsheet can all address the breakdown by position.
    /// </summary>
    public IReadOnlyList<BrandColumn> BrandColumns { get; private set; } = new List<BrandColumn>();

    /// <summary>
    /// Raised after <see cref="BrandColumns"/> changes. WPF cannot declare a variable number of
    /// DataGrid columns in XAML, so the view rebuilds them in code-behind when this fires.
    /// </summary>
    public event EventHandler? BrandColumnsChanged;

    private string _filter = string.Empty;
    public string Filter { get => _filter; set { if (SetProperty(ref _filter, value)) ApplyFilter(); } }

    private bool _shortfallOnly;
    public bool ShortfallOnly { get => _shortfallOnly; set { if (SetProperty(ref _shortfallOnly, value)) ApplyFilter(); } }

    public override void OnActivated() => Load();

    [RelayCommand]
    private void Load()
    {
        using var scope = _scopes.Create();
        var view = scope.Reports.GetFinishedStock();
        _allFinished = view.Rows.ToList();
        _allAccessory = scope.Reports.GetAccessoryStock().Rows.ToList();

        // Brands are admin-managed and rarely change, but a newly added one has to appear without
        // restarting the app, so the columns are rebuilt on every refresh rather than once.
        // GetStockBrandColumns() considers both finished and accessory packed stock, so the same
        // list is correct for both grids.
        BrandColumns = view.Brands;
        BrandColumnsChanged?.Invoke(this, EventArgs.Empty);

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var f = Filter?.Trim() ?? string.Empty;
        IEnumerable<StockRow> fin = _allFinished;
        if (f.Length > 0)
            fin = fin.Where(r =>
                r.ItemCode.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                r.ItemName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                r.Colour.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                r.Grade.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                // Match a brand name only where that brand actually holds packed stock on the row,
                // so filtering by brand narrows to rows that brand is really involved in.
                r.PackedByBrand.Any(b => b.Packed != 0 &&
                                         b.Brand.Contains(f, StringComparison.OrdinalIgnoreCase)));
        if (ShortfallOnly) fin = fin.Where(r => r.Available < 0);

        Finished.Clear();
        foreach (var r in fin) Finished.Add(r);

        IEnumerable<AccessoryStockRow> acc = _allAccessory;
        if (f.Length > 0)
            acc = acc.Where(r =>
                r.AccessoryCode.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                r.AccessoryName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                r.PackedByBrand.Any(b => b.Packed != 0 &&
                                         b.Brand.Contains(f, StringComparison.OrdinalIgnoreCase)));
        if (ShortfallOnly) acc = acc.Where(r => r.Available < 0);

        Accessories.Clear();
        foreach (var r in acc) Accessories.Add(r);
    }

    /// <summary>The rows currently on screen, packaged with the brand columns they are aligned to.</summary>
    private FinishedStockView CurrentView() => new(BrandColumns, Finished.ToList());

    /// <summary>Accessory analogue of <see cref="CurrentView"/>.</summary>
    private AccessoryStockView CurrentAccessoryView() => new(BrandColumns, Accessories.ToList());

    // ---- Exports -------------------------------------------------------------

    /// <summary>
    /// True while the Accessories tab is showing, bound two-way from that tab's IsSelected.
    /// The export buttons live in the toolbar above the tabs, so this is what tells them which of
    /// the two grids the user actually means — without it they silently save the other one.
    /// </summary>
    private bool _showingAccessories;
    public bool ShowingAccessories { get => _showingAccessories; set => SetProperty(ref _showingAccessories, value); }

    /// <summary>Base file name for the grid being exported, so the two never overwrite each other.</summary>
    private string ExportFileStem =>
        (ShowingAccessories ? "AccessoryStock" : "FinishedStock") + $"_{DateTime.Now:yyyyMMdd}";

    /// <summary>What was saved, named explicitly so the user can see the right grid was picked up.</summary>
    private string ExportLabel => ShowingAccessories ? "Accessory stock" : "Finished goods stock";

    [RelayCommand]
    private void ExportPdf()
    {
        var path = _dialogs.SaveFile("PDF file (*.pdf)|*.pdf", $"{ExportFileStem}.pdf");
        if (path is null) return;
        try
        {
            if (ShowingAccessories)
                PdfReports.SaveAccessoryStock(CurrentAccessoryView(), path, DateTime.Today);
            else
                PdfReports.SaveFinishedStock(CurrentView(), path, DateTime.Today);

            _dialogs.Info($"{ExportLabel} report saved.");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private void ExportExcel()
    {
        var path = _dialogs.SaveFile("Excel file (*.xlsx)|*.xlsx", $"{ExportFileStem}.xlsx");
        if (path is null) return;
        try
        {
            if (ShowingAccessories)
                // Hand-laid out, same as the finished grid below — the generic reflection exporter
                // would drop the per-brand breakdown on the floor.
                ExcelReports.SaveAccessoryStock(CurrentAccessoryView(), path);
            else
                // Hand-laid out rather than the generic reflection exporter, which would drop the
                // per-brand breakdown on the floor.
                ExcelReports.SaveFinishedStock(CurrentView(), path);

            _dialogs.Info($"{ExportLabel} exported to Excel.");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }
}
