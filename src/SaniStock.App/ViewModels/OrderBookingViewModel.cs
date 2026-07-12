using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaniStock.App.Infrastructure;
using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;

namespace SaniStock.App.ViewModels;

/// <summary>A staged finished-ware line on the order being built.</summary>
public record BookingLine(int ItemId, int GradeId, int ColourId, string Item, string Grade, string Colour, decimal Quantity);
/// <summary>A staged accessory line on the order being built.</summary>
public record BookingAccessoryLine(int AccessoryId, string Accessory, decimal Quantity);
/// <summary>A recently booked order summary.</summary>
public record RecentOrderRow(string OrderNo, DateTime OrderDate, string Party, string Status, int LineCount);

public partial class OrderBookingViewModel : ViewModelBase
{
    private readonly IDomainScopeFactory _scopes;
    private readonly IDialogService _dialogs;

    public OrderBookingViewModel(IDomainScopeFactory scopes, IDialogService dialogs)
    {
        _scopes = scopes;
        _dialogs = dialogs;
        Title = "Order Booking";
    }

    public ObservableCollection<Party> Parties { get; } = new();
    public ObservableCollection<Item> Items { get; } = new();
    public ObservableCollection<Grade> Grades { get; } = new();
    public ObservableCollection<Colour> Colours { get; } = new();
    public ObservableCollection<Accessory> Accessories { get; } = new();

    public ObservableCollection<BookingLine> Lines { get; } = new();
    public ObservableCollection<BookingAccessoryLine> AccessoryLines { get; } = new();
    public ObservableCollection<RecentOrderRow> RecentOrders { get; } = new();

    [ObservableProperty] private Party? _party;
    [ObservableProperty] private DateTime _orderDate = DateTime.Today;
    [ObservableProperty] private string _remarks = string.Empty;

    // add-line fields
    [ObservableProperty] private Item? _newItem;
    [ObservableProperty] private Grade? _newGrade;
    [ObservableProperty] private Colour? _newColour;
    [ObservableProperty] private string _newQty = string.Empty;

    // add-accessory fields
    [ObservableProperty] private Accessory? _newAccessory;
    [ObservableProperty] private string _newAccessoryQty = string.Empty;

    public override void OnActivated() => Load();

    [RelayCommand]
    private void Load()
    {
        using var scope = _scopes.Create();
        Fill(Parties, scope.Db.Parties.Where(x => x.IsActive).OrderBy(x => x.Name).ToList());
        Fill(Items, scope.Db.Items.Where(x => x.IsActive).OrderBy(x => x.Name).ToList());
        Fill(Grades, scope.Db.Grades.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToList());
        Fill(Colours, scope.Db.Colours.Where(x => x.IsActive).OrderBy(x => x.Name).ToList());
        Fill(Accessories, scope.Db.Accessories.Where(x => x.IsActive).OrderBy(x => x.Name).ToList());
        LoadRecent(scope);
    }

    private void LoadRecent(DomainScope scope)
    {
        var rows =
            (from o in scope.Db.Orders
             join p in scope.Db.Parties on o.PartyId equals p.Id
             orderby o.Id descending
             select new RecentOrderRow(o.OrderNo, o.OrderDate, p.Name, o.Status.ToString(),
                 o.Lines.Count + o.AccessoryLines.Count))
            .Take(50).ToList();
        RecentOrders.Clear();
        foreach (var r in rows) RecentOrders.Add(r);
    }

    [RelayCommand]
    private void AddLine()
    {
        if (NewItem is null || NewGrade is null || NewColour is null) { _dialogs.Error("Select item, grade and colour."); return; }
        if (!decimal.TryParse(NewQty, out var q) || q <= 0) { _dialogs.Error("Enter a quantity greater than zero."); return; }
        Lines.Add(new BookingLine(NewItem.Id, NewGrade.Id, NewColour.Id, NewItem.Name, NewGrade.Name, NewColour.Name, q));
        NewQty = string.Empty;
    }

    [RelayCommand]
    private void RemoveLine(BookingLine? line) { if (line is not null) Lines.Remove(line); }

    [RelayCommand]
    private void AddAccessoryLine()
    {
        if (NewAccessory is null) { _dialogs.Error("Select an accessory."); return; }
        if (!decimal.TryParse(NewAccessoryQty, out var q) || q <= 0) { _dialogs.Error("Enter a quantity greater than zero."); return; }
        AccessoryLines.Add(new BookingAccessoryLine(NewAccessory.Id, NewAccessory.Name, q));
        NewAccessoryQty = string.Empty;
    }

    [RelayCommand]
    private void RemoveAccessoryLine(BookingAccessoryLine? line) { if (line is not null) AccessoryLines.Remove(line); }

    [RelayCommand]
    private void Book()
    {
        if (Party is null) { _dialogs.Error("Select a party."); return; }
        if (Lines.Count == 0 && AccessoryLines.Count == 0) { _dialogs.Error("Add at least one line."); return; }
        try
        {
            using var scope = _scopes.Create();
            var input = new OrderInput(Party.Id, OrderDate, string.IsNullOrWhiteSpace(Remarks) ? null : Remarks.Trim(),
                Lines.Select(l => new OrderLineInput(l.ItemId, l.GradeId, l.ColourId, l.Quantity)).ToList(),
                AccessoryLines.Select(a => new OrderAccessoryLineInput(a.AccessoryId, a.Quantity)).ToList());
            var order = scope.Orders.Book(input);
            _dialogs.Info($"Order {order.OrderNo} booked.");
            Lines.Clear();
            AccessoryLines.Clear();
            Remarks = string.Empty;
            LoadRecent(scope);
        }
        catch (DomainException ex) { _dialogs.Error(ex.Message); }
        catch (Exception ex) { _dialogs.Error("Unexpected error: " + ex.Message); }
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var x in source) target.Add(x);
    }
}
