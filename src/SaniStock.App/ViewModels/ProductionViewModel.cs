using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SaniStock.App.Infrastructure;
using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;

namespace SaniStock.App.ViewModels;

/// <summary>A finished-production history row with the id needed to reverse it.</summary>
public record ProductionHistoryRow(int Id, DateTime Date, string Item, string Grade, string Colour,
    decimal Quantity, bool IsReversal, string CreatedBy);

public partial class ProductionViewModel : ViewModelBase
{
    private readonly IDomainScopeFactory _scopes;
    private readonly IDialogService _dialogs;

    public ProductionViewModel(IDomainScopeFactory scopes, IDialogService dialogs)
    {
        _scopes = scopes;
        _dialogs = dialogs;
        Title = "Production & Stock-In";
    }

    // Lookups
    public ObservableCollection<Item> Items { get; } = new();
    public ObservableCollection<Grade> Grades { get; } = new();
    public ObservableCollection<Colour> Colours { get; } = new();
    public ObservableCollection<Accessory> Accessories { get; } = new();
    public ObservableCollection<RawMaterial> RawMaterials { get; } = new();
    public ObservableCollection<ProductionHistoryRow> History { get; } = new();

    // Finished production form
    [ObservableProperty] private Item? _prodItem;
    [ObservableProperty] private Grade? _prodGrade;
    [ObservableProperty] private Colour? _prodColour;
    [ObservableProperty] private DateTime _prodDate = DateTime.Today;
    [ObservableProperty] private string _prodQty = string.Empty;
    [ObservableProperty] private string _prodRemarks = string.Empty;

    // Accessory receipt form
    [ObservableProperty] private Accessory? _accItem;
    [ObservableProperty] private DateTime _accDate = DateTime.Today;
    [ObservableProperty] private string _accQty = string.Empty;
    [ObservableProperty] private string _accRemarks = string.Empty;

    // Green ware form
    [ObservableProperty] private Item? _greenItem;
    [ObservableProperty] private Colour? _greenColour;
    [ObservableProperty] private DateTime _greenDate = DateTime.Today;
    [ObservableProperty] private bool _greenIsIssue;
    [ObservableProperty] private string _greenQty = string.Empty;

    // Raw material form
    [ObservableProperty] private RawMaterial? _rawItem;
    [ObservableProperty] private DateTime _rawDate = DateTime.Today;
    [ObservableProperty] private bool _rawIsIssue;
    [ObservableProperty] private string _rawQty = string.Empty;

    public override void OnActivated() => Load();

    [RelayCommand]
    private void Load()
    {
        using var scope = _scopes.Create();
        Fill(Items, scope.Db.Items.Where(x => x.IsActive).OrderBy(x => x.Name));
        Fill(Grades, scope.Db.Grades.Where(x => x.IsActive).OrderBy(x => x.SortOrder));
        Fill(Colours, scope.Db.Colours.Where(x => x.IsActive).OrderBy(x => x.Name));
        Fill(Accessories, scope.Db.Accessories.Where(x => x.IsActive).OrderBy(x => x.Name));
        Fill(RawMaterials, scope.Db.RawMaterials.Where(x => x.IsActive).OrderBy(x => x.Name));
        LoadHistory(scope);
    }

    private void LoadHistory(DomainScope scope)
    {
        var rows =
            (from e in scope.Db.ProductionEntries
             join i in scope.Db.Items on e.ItemId equals i.Id
             join g in scope.Db.Grades on e.GradeId equals g.Id
             join c in scope.Db.Colours on e.ColourId equals c.Id
             orderby e.Id descending
             select new ProductionHistoryRow(e.Id, e.Date, i.Name, g.Name, c.Name, e.Quantity, e.IsReversal, e.CreatedBy))
            .Take(100).ToList();
        History.Clear();
        foreach (var r in rows) History.Add(r);
    }

    [RelayCommand]
    private void PostProduction()
    {
        if (ProdItem is null || ProdGrade is null || ProdColour is null)
        { _dialogs.Error("Select item, grade and colour."); return; }
        if (!TryQty(ProdQty, out var qty)) return;
        Run(scope =>
        {
            scope.Production.Post(new ProductionInput(ProdDate, ProdItem.Id, ProdGrade.Id, ProdColour.Id, qty, NullIfBlank(ProdRemarks)));
            _dialogs.Info($"Posted production of {qty:0.###} {ProdItem.UnitOfMeasure}.");
            ProdQty = string.Empty; ProdRemarks = string.Empty;
        });
    }

    [RelayCommand]
    private void ReverseProduction(ProductionHistoryRow? row)
    {
        if (row is null) return;
        if (row.IsReversal) { _dialogs.Error("A reversal entry cannot be reversed."); return; }
        if (!_dialogs.Confirm($"Reverse production of {row.Quantity:0.###} — {row.Item} / {row.Grade} / {row.Colour}?"))
            return;
        Run(scope => { scope.Production.Reverse(row.Id); _dialogs.Info("Production reversed."); });
    }

    [RelayCommand]
    private void PostAccessory()
    {
        if (AccItem is null) { _dialogs.Error("Select an accessory."); return; }
        if (!TryQty(AccQty, out var qty)) return;
        Run(scope =>
        {
            scope.AccessoryReceipts.Post(new AccessoryReceiptInput(AccDate, AccItem.Id, qty, NullIfBlank(AccRemarks)));
            _dialogs.Info($"Received {qty:0.###} {AccItem.Name}.");
            AccQty = string.Empty; AccRemarks = string.Empty;
        });
    }

    [RelayCommand]
    private void PostGreen()
    {
        if (GreenItem is null || GreenColour is null) { _dialogs.Error("Select item and colour."); return; }
        if (!TryQty(GreenQty, out var qty)) return;
        Run(scope =>
        {
            scope.Green.Post(new GreenPieceInput(GreenDate, GreenItem.Id, GreenColour.Id, GreenIsIssue, qty, null));
            _dialogs.Info("Green ware movement posted.");
            GreenQty = string.Empty;
        });
    }

    [RelayCommand]
    private void PostRaw()
    {
        if (RawItem is null) { _dialogs.Error("Select a raw material."); return; }
        if (!TryQty(RawQty, out var qty)) return;
        Run(scope =>
        {
            scope.Raw.Post(new RawMaterialInput(RawDate, RawItem.Id, RawIsIssue, qty, null));
            _dialogs.Info("Raw material movement posted.");
            RawQty = string.Empty;
        });
    }

    private void Run(Action<DomainScope> action)
    {
        try
        {
            using var scope = _scopes.Create();
            action(scope);
            LoadHistory(scope);
        }
        catch (DomainException ex) { _dialogs.Error(ex.Message); }
        catch (Exception ex) { _dialogs.Error("Unexpected error: " + ex.Message); }
    }

    private bool TryQty(string text, out decimal qty)
    {
        if (decimal.TryParse(text, out qty) && qty > 0) return true;
        _dialogs.Error("Enter a quantity greater than zero.");
        return false;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Fill<T>(ObservableCollection<T> target, IQueryable<T> source)
    {
        target.Clear();
        foreach (var x in source.ToList()) target.Add(x);
    }
}
