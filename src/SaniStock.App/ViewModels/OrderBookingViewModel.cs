using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaniStock.App.Infrastructure;
using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;

namespace SaniStock.App.ViewModels;

/// <summary>A staged finished-ware line on the order being built, with its bundled accessories.</summary>
public partial class BookingLine : ObservableObject
{
    public int ItemId { get; init; }
    public int GradeId { get; init; }
    public int ColourId { get; init; }
    public int BrandId { get; init; }
    public string Item { get; init; } = string.Empty;
    public string Grade { get; init; } = string.Empty;
    public string Colour { get; init; } = string.Empty;
    public string Brand { get; init; } = string.Empty;
    public decimal Quantity { get; init; }

    /// <summary>Default accessories auto-attached to this line; each can be included or excluded.</summary>
    public ObservableCollection<BookingLineAccessory> Accessories { get; } = new();
    public bool HasAccessories => Accessories.Count > 0;
}

/// <summary>A default accessory auto-attached under a booking line, with a per-line include toggle.</summary>
public partial class BookingLineAccessory : ObservableObject
{
    public int AccessoryId { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    [ObservableProperty] private bool _include = true;
}

/// <summary>A staged standalone (manually-added) accessory line on the order being built.</summary>
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
    /// <summary>Active brands only — an order is always placed for a brand still being sold.</summary>
    public ObservableCollection<Brand> Brands { get; } = new();
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
    [ObservableProperty] private Brand? _newBrand;
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
        Fill(Brands, scope.Db.Brands.Where(x => x.IsActive).OrderBy(x => x.Name).ToList());
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
        if (NewItem is null || NewGrade is null || NewColour is null || NewBrand is null)
        { _dialogs.Error("Select item, grade, colour and brand."); return; }
        if (!decimal.TryParse(NewQty, out var q) || q <= 0) { _dialogs.Error("Enter a quantity greater than zero."); return; }

        var line = new BookingLine
        {
            ItemId = NewItem.Id, GradeId = NewGrade.Id, ColourId = NewColour.Id, BrandId = NewBrand.Id,
            Item = NewItem.Name, Grade = NewGrade.Name, Colour = NewColour.Name, Brand = NewBrand.Name,
            Quantity = q
        };

        // Auto-populate this line's default accessories (recipe × ordered qty), each included by default.
        using (var scope = _scopes.Create())
        {
            foreach (var d in scope.Master.GetItemAccessoryDefaults(NewItem.Id).Where(d => d.IsActive))
            {
                line.Accessories.Add(new BookingLineAccessory
                {
                    AccessoryId = d.AccessoryId,
                    Name = d.Accessory?.Name ?? "Accessory",
                    Quantity = d.QtyPerUnit * q
                });
            }
        }

        Lines.Add(line);
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
                Lines.Select(l => new OrderLineInput(l.ItemId, l.GradeId, l.ColourId, l.BrandId, l.Quantity,
                    l.Accessories.Where(a => !a.Include).Select(a => a.AccessoryId).ToList())).ToList(),
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
