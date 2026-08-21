using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using SaniStock.App.Infrastructure;
using SaniStock.Domain.Models;

namespace SaniStock.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IDomainScopeFactory _scopes;

    public DashboardViewModel(IDomainScopeFactory scopes)
    {
        _scopes = scopes;
        Title = "Dashboard";
    }

    public ObservableCollection<ShortfallRow> TopShortfalls { get; } = new();
    public ObservableCollection<StockRow> LowStock { get; } = new();

    private decimal _todayProduction;
    public decimal TodayProduction { get => _todayProduction; set => SetProperty(ref _todayProduction, value); }

    private int _todayDispatches;
    public int TodayDispatches { get => _todayDispatches; set => SetProperty(ref _todayDispatches, value); }

    private int _shortfallCount;
    public int ShortfallCount { get => _shortfallCount; set => SetProperty(ref _shortfallCount, value); }

    private int _lowStockCount;
    public int LowStockCount { get => _lowStockCount; set => SetProperty(ref _lowStockCount, value); }

    private int _openOrders;
    public int OpenOrders { get => _openOrders; set => SetProperty(ref _openOrders, value); }

    public string Today => DateTime.Now.ToString("dddd, dd MMMM yyyy");

    public override void OnActivated() => Load();

    [RelayCommand]
    private void Load()
    {
        using var scope = _scopes.Create();
        var d = scope.Reports.GetDashboard();
        TodayProduction = d.TodayProductionQty;
        TodayDispatches = d.TodayDispatchCount;
        ShortfallCount = d.ShortfallCount;
        LowStockCount = d.LowStockCount;
        OpenOrders = d.OpenOrderCount;

        TopShortfalls.Clear();
        foreach (var s in scope.Reports.GetShortfall())
            TopShortfalls.Add(s);

        LowStock.Clear();
        foreach (var s in scope.Reports.GetFinishedStock().Rows
                     .Where(r => r.OnHand > 0 && r.OnHand <= scope.Reports.LowStockThreshold)
                     .OrderBy(r => r.OnHand))
            LowStock.Add(s);
    }
}
