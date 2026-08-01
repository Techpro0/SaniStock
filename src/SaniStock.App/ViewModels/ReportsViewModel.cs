using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaniStock.App.Infrastructure;
using SaniStock.Data.Entities;
using SaniStock.Domain.Models;
using SaniStock.Reports;

namespace SaniStock.App.ViewModels;

public partial class ReportsViewModel : ViewModelBase
{
    private readonly IDomainScopeFactory _scopes;
    private readonly IDialogService _dialogs;

    public ReportsViewModel(IDomainScopeFactory scopes, IDialogService dialogs)
    {
        _scopes = scopes;
        _dialogs = dialogs;
        Title = "Reports";
    }

    public ObservableCollection<Party> Parties { get; } = new();
    public ObservableCollection<Item> Items { get; } = new();

    public ObservableCollection<StockRow> StockRows { get; } = new();

    /// <summary>
    /// The brand columns the loaded stock rows are aligned to, kept so the exports lay the same
    /// columns out as the Stock screen. The Reports tab shows the totals only, so it does not need
    /// dynamic grid columns of its own.
    /// </summary>
    private IReadOnlyList<BrandColumn> _stockBrands = new List<BrandColumn>();
    public ObservableCollection<ShortfallRow> ShortfallRows { get; } = new();
    public ObservableCollection<ProductionReportRow> ProductionRows { get; } = new();
    public ObservableCollection<OrderReportRow> OrderRows { get; } = new();

    [ObservableProperty] private DateTime _fromDate = DateTime.Today.AddMonths(-1);
    [ObservableProperty] private DateTime _toDate = DateTime.Today;
    [ObservableProperty] private Party? _party;   // null => all
    [ObservableProperty] private Item? _item;     // null => all

    public override void OnActivated()
    {
        using var scope = _scopes.Create();
        Fill(Parties, scope.Db.Parties.OrderBy(x => x.Name).ToList());
        Fill(Items, scope.Db.Items.OrderBy(x => x.Name).ToList());
        RunStock();
    }

    [RelayCommand]
    private void RunStock()
    {
        using var scope = _scopes.Create();
        var view = scope.Reports.GetFinishedStock();
        _stockBrands = view.Brands;
        Fill(StockRows, view.Rows);
    }

    [RelayCommand]
    private void RunShortfall()
    {
        using var scope = _scopes.Create();
        Fill(ShortfallRows, scope.Reports.GetShortfall());
    }

    [RelayCommand]
    private void RunProduction()
    {
        using var scope = _scopes.Create();
        Fill(ProductionRows, scope.Reports.GetProduction(FromDate, ToDate, Item?.Id));
    }

    [RelayCommand]
    private void RunOrders()
    {
        using var scope = _scopes.Create();
        Fill(OrderRows, scope.Reports.GetOrders(FromDate, ToDate, Party?.Id));
    }

    // ---- Exports ----
    // Both stock exports go through the brand-aware writers: the generic reflection exporter would
    // quietly drop the per-brand packed breakdown, which is most of the point of this report now.
    [RelayCommand] private void StockPdf() => Pdf("Stock", p => PdfReports.SaveFinishedStock(StockView(), p, DateTime.Today));
    [RelayCommand] private void StockExcel() => Excel("Stock", p => ExcelReports.SaveFinishedStock(StockView(), p, "Stock"));
    [RelayCommand] private void ShortfallPdf() => Pdf("Shortfall", p => PdfReports.SaveShortfall(ShortfallRows.ToList(), p));
    [RelayCommand] private void ProductionPdf() => Pdf("Production", p => PdfReports.SaveProduction(ProductionRows.ToList(), p, FromDate, ToDate));
    [RelayCommand] private void ProductionExcel() => Excel("Production", p => ExcelExporter.Save(ProductionRows.ToList(), p, "Production"));
    [RelayCommand] private void OrdersPdf() => Pdf("Orders", p => PdfReports.SaveOrders(OrderRows.ToList(), p, FromDate, ToDate));
    [RelayCommand] private void OrdersExcel() => Excel("Orders", p => ExcelReports.SaveOrders(OrderRows.ToList(), p, "Orders"));

    private FinishedStockView StockView() => new(_stockBrands, StockRows.ToList());

    private void Pdf(string name, Action<string> save)
    {
        var path = _dialogs.SaveFile("PDF file (*.pdf)|*.pdf", $"{name}_{DateTime.Now:yyyyMMdd}.pdf");
        if (path is null) return;
        try { save(path); _dialogs.Info($"{name} report saved."); }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    private void Excel(string name, Action<string> save)
    {
        var path = _dialogs.SaveFile("Excel file (*.xlsx)|*.xlsx", $"{name}_{DateTime.Now:yyyyMMdd}.xlsx");
        if (path is null) return;
        try { save(path); _dialogs.Info($"{name} exported to Excel."); }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var x in source) target.Add(x);
    }
}
