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

    private string _filter = string.Empty;
    public string Filter { get => _filter; set { if (SetProperty(ref _filter, value)) ApplyFilter(); } }

    private bool _shortfallOnly;
    public bool ShortfallOnly { get => _shortfallOnly; set { if (SetProperty(ref _shortfallOnly, value)) ApplyFilter(); } }

    public override void OnActivated() => Load();

    [RelayCommand]
    private void Load()
    {
        using var scope = _scopes.Create();
        _allFinished = scope.Reports.GetFinishedStock();
        _allAccessory = scope.Reports.GetAccessoryStock();
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
                r.Grade.Contains(f, StringComparison.OrdinalIgnoreCase));
        if (ShortfallOnly) fin = fin.Where(r => r.Available < 0);

        Finished.Clear();
        foreach (var r in fin) Finished.Add(r);

        IEnumerable<AccessoryStockRow> acc = _allAccessory;
        if (f.Length > 0)
            acc = acc.Where(r =>
                r.AccessoryCode.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                r.AccessoryName.Contains(f, StringComparison.OrdinalIgnoreCase));
        if (ShortfallOnly) acc = acc.Where(r => r.Available < 0);

        Accessories.Clear();
        foreach (var r in acc) Accessories.Add(r);
    }

    [RelayCommand]
    private void ExportPdf()
    {
        var path = _dialogs.SaveFile("PDF file (*.pdf)|*.pdf", $"FinishedStock_{DateTime.Now:yyyyMMdd}.pdf");
        if (path is null) return;
        try
        {
            PdfReports.SaveFinishedStock(Finished.ToList(), path, DateTime.Today);
            _dialogs.Info("Stock report saved.");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private void ExportExcel()
    {
        var path = _dialogs.SaveFile("Excel file (*.xlsx)|*.xlsx", $"FinishedStock_{DateTime.Now:yyyyMMdd}.xlsx");
        if (path is null) return;
        try
        {
            ExcelExporter.Save(Finished.ToList(), path, "Finished Stock");
            _dialogs.Info("Stock exported to Excel.");
        }
        catch (Exception ex) { _dialogs.Error(ex.Message); }
    }
}
