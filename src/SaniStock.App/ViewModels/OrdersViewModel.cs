using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SaniStock.App.Infrastructure;
using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;

namespace SaniStock.App.ViewModels;

/// <summary>One order in the list, with what the row's buttons need to know.</summary>
public record OrderListRow(int Id, string OrderNo, DateTime OrderDate, string Party,
    OrderStatus Status, int LineCount, decimal Ordered, decimal Pending, bool NothingSent)
{
    public string StatusText => Status switch
    {
        OrderStatus.PartiallyDispatched => "Part sent",
        OrderStatus.Dispatched => "Sent",
        OrderStatus.Cancelled => "Deleted",
        _ => "Booked"
    };

    /// <summary>
    /// Editing re-plans the whole reservation from scratch, which is only meaningful while none of
    /// it has been consumed — so it is offered only on a live order with nothing sent.
    /// </summary>
    public bool CanEdit => NothingSent && Status != OrderStatus.Cancelled;

    /// <summary>Delete is Cancel: there must be a reservation left to release.</summary>
    public bool CanDelete => Status is OrderStatus.Booked or OrderStatus.PartiallyDispatched;
}

/// <summary>An item line on the opened order, with the stock it is actually holding.</summary>
public record OrderLineDetailRow(int Id, string Item, string Grade, string Colour, string Brand,
    decimal Ordered, decimal Dispatched, decimal Pending, string Sources);

/// <summary>An accessory line on the opened order.</summary>
public record OrderAccessoryDetailRow(string Accessory, string BundledWith,
    decimal Ordered, decimal Dispatched, decimal Pending);

/// <summary>A dispatch already posted against the opened order.</summary>
public record OrderDispatchDetailRow(int Id, string DispatchNo, DateTime Date, string DispatchedBy,
    decimal Quantity, bool IsReversal, bool IsReversed, bool IsLatestLive)
{
    /// <summary>Shown negative on a reversing note, since it put goods back rather than sending them.</summary>
    public decimal SignedQuantity => IsReversal ? -Quantity : Quantity;

    public string Kind => IsReversal ? "Reversal" : IsReversed ? "Reversed" : "Sent";

    /// <summary>
    /// Only the newest note still standing can be undone. An earlier one has had later dispatches
    /// consume allocations on top of it, so unwinding out of turn would give quantity back to the
    /// wrong sources — they have to come off newest-first.
    /// </summary>
    public bool CanReverse => IsLatestLive;
}

/// <summary>A line being edited. Quantity stays a string until saved, like every typed number here.</summary>
public partial class OrderEditLineRow : ObservableObject
{
    [ObservableProperty] private Item? _item;
    [ObservableProperty] private Grade? _grade;
    [ObservableProperty] private Colour? _colour;
    [ObservableProperty] private Brand? _brand;
    [ObservableProperty] private string _quantity = string.Empty;
}

/// <summary>
/// Browse every order, open one to see exactly what it is holding, and edit or delete it.
/// <para>
/// Separate from <see cref="OrderBookingViewModel"/>, which stays a booking-only workflow. Delete
/// here is <c>OrderService.Cancel</c> and Edit is <c>OrderService.Edit</c> — neither reimplements
/// anything, and both keep the order and its history on record.
/// </para>
/// </summary>
public partial class OrdersViewModel : ViewModelBase
{
    /// <summary>
    /// Ceiling on rows fetched. The grid virtualises rendering, but the query itself is unbounded,
    /// and a plant with years of history should not pull all of it into memory to look at last
    /// week. Hitting the cap says so rather than silently truncating.
    /// </summary>
    private const int MaxRows = 500;

    private readonly IDomainScopeFactory _scopes;
    private readonly IDialogService _dialogs;

    public OrdersViewModel(IDomainScopeFactory scopes, IDialogService dialogs)
    {
        _scopes = scopes;
        _dialogs = dialogs;
        Title = "Orders";
    }

    // ---- Filters --------------------------------------------------------------

    /// <summary>Status choices, led by an "All" sentinel (null).</summary>
    public ObservableCollection<string> StatusOptions { get; } =
        new() { "All", "Booked", "Part sent", "Sent", "Deleted" };

    public ObservableCollection<Party> PartyOptions { get; } = new();
    public ObservableCollection<Brand> BrandOptions { get; } = new();

    /// <summary>
    /// Set while several filters are being reset at once, so the list reloads once at the end
    /// rather than after each one.
    /// </summary>
    private bool _suspendReload;

    /// <summary>
    /// Reloads after a filter changes — deferred, never inline. A filter setter fires from a
    /// binding write-back, including the one WPF performs while tearing this screen down on
    /// navigation; reloading there would replace collections mid-teardown. See
    /// <see cref="ViewModelBase.RunAfterLayout"/>.
    /// </summary>
    private void ReloadUnlessSuspended()
    {
        if (_suspendReload) return;
        RunAfterLayout(Load);
    }

    private string _statusFilter = "All";
    public string StatusFilter { get => _statusFilter; set { if (SetProperty(ref _statusFilter, value)) ReloadUnlessSuspended(); } }

    private Party? _partyFilter;
    public Party? PartyFilter { get => _partyFilter; set { if (SetProperty(ref _partyFilter, value)) ReloadUnlessSuspended(); } }

    private Brand? _brandFilter;
    public Brand? BrandFilter { get => _brandFilter; set { if (SetProperty(ref _brandFilter, value)) ReloadUnlessSuspended(); } }

    [ObservableProperty] private DateTime _fromDate = DateTime.Today.AddMonths(-3);
    [ObservableProperty] private DateTime _toDate = DateTime.Today;

    partial void OnFromDateChanged(DateTime value) => ReloadUnlessSuspended();
    partial void OnToDateChanged(DateTime value) => ReloadUnlessSuspended();

    [ObservableProperty] private string _resultSummary = string.Empty;

    // ---- List + detail --------------------------------------------------------

    public ObservableCollection<OrderListRow> Orders { get; } = new();
    public ObservableCollection<OrderLineDetailRow> DetailLines { get; } = new();
    public ObservableCollection<OrderAccessoryDetailRow> DetailAccessories { get; } = new();
    public ObservableCollection<OrderDispatchDetailRow> DetailDispatches { get; } = new();

    [ObservableProperty] private string _detailTitle = "Select an order and press View.";
    [ObservableProperty] private bool _hasDetail;

    /// <summary>The order currently open in the detail pane — not necessarily the selected row.</summary>
    [ObservableProperty] private OrderListRow? _openOrder;

    // ---- Edit mode ------------------------------------------------------------

    public ObservableCollection<Item> Items { get; } = new();
    public ObservableCollection<Grade> Grades { get; } = new();
    public ObservableCollection<Colour> Colours { get; } = new();
    public ObservableCollection<Brand> Brands { get; } = new();
    public ObservableCollection<OrderEditLineRow> EditLines { get; } = new();

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private Party? _editParty;
    [ObservableProperty] private DateTime _editDate = DateTime.Today;
    [ObservableProperty] private string _editRemarks = string.Empty;

    public override void OnActivated()
    {
        using (var scope = _scopes.Create())
        {
            Fill(PartyOptions, scope.Db.Parties.OrderBy(x => x.Name).ToList());
            Fill(BrandOptions, scope.Db.Brands.OrderBy(x => x.Name).ToList());
            Fill(Items, scope.Db.Items.Where(x => x.IsActive).OrderBy(x => x.Name).ToList());
            Fill(Grades, scope.Db.Grades.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToList());
            Fill(Colours, scope.Db.Colours.Where(x => x.IsActive).OrderBy(x => x.Name).ToList());
            Fill(Brands, scope.Db.Brands.Where(x => x.IsActive).OrderBy(x => x.Name).ToList());
        }
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        using var scope = _scopes.Create();

        var q = scope.Db.Orders.AsNoTracking()
            .Where(o => o.OrderDate >= FromDate.Date && o.OrderDate < ToDate.Date.AddDays(1));

        var wanted = StatusFilter switch
        {
            "Booked" => OrderStatus.Booked,
            "Part sent" => OrderStatus.PartiallyDispatched,
            "Sent" => OrderStatus.Dispatched,
            "Deleted" => OrderStatus.Cancelled,
            _ => (OrderStatus?)null
        };
        if (wanted is OrderStatus s) q = q.Where(o => o.Status == s);
        if (PartyFilter is { Id: > 0 }) q = q.Where(o => o.PartyId == PartyFilter.Id);
        // Brand lives on the line, so filter orders that have at least one live line for it.
        if (BrandFilter is { Id: > 0 })
            q = q.Where(o => o.Lines.Any(l => l.BrandId == BrandFilter.Id && l.QuantityOrdered > 0));

        var total = q.Count();

        var rows =
            (from o in q
             join p in scope.Db.Parties on o.PartyId equals p.Id
             orderby o.Id descending
             select new OrderListRow(
                 o.Id, o.OrderNo, o.OrderDate, p.Name, o.Status,
                 o.Lines.Count(l => l.QuantityOrdered > 0) + o.AccessoryLines.Count(a => a.QuantityOrdered > 0),
                 o.Lines.Where(l => l.QuantityOrdered > 0).Sum(l => (decimal?)l.QuantityOrdered) ?? 0m,
                 o.Lines.Where(l => l.QuantityOrdered > 0)
                     .Sum(l => (decimal?)(l.QuantityOrdered - l.QuantityDispatched)) ?? 0m,
                 o.Lines.All(l => l.QuantityDispatched == 0) && o.AccessoryLines.All(a => a.QuantityDispatched == 0)))
            .Take(MaxRows).ToList();

        Fill(Orders, rows);
        ResultSummary = total > rows.Count
            ? $"Showing the {rows.Count} most recent of {total} orders — narrow the dates or filters to see the rest."
            : $"{total} order(s).";

        // Keep the open order in step with what was just reloaded.
        if (OpenOrder is not null)
        {
            var again = Orders.FirstOrDefault(o => o.Id == OpenOrder.Id);
            if (again is not null) OpenDetail(again, scope);
            else ClearDetail();
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suspendReload = true;
        try
        {
            StatusFilter = "All";
            PartyFilter = null;
            BrandFilter = null;
            FromDate = DateTime.Today.AddMonths(-3);
            ToDate = DateTime.Today;
        }
        finally { _suspendReload = false; }
        Load();
    }

    // ---- View -----------------------------------------------------------------

    [RelayCommand]
    private void View(OrderListRow? row)
    {
        if (row is null) return;
        using var scope = _scopes.Create();
        OpenDetail(row, scope);
    }

    /// <summary>
    /// Loads everything about one order: its live lines with the grade+brand rows each is actually
    /// holding stock against, its accessory lines, and every dispatch posted against it. The
    /// allocation sources are the bit that explains a shortfall — they show where the reservation
    /// really sits, which is not always the grade printed on the line.
    /// </summary>
    private void OpenDetail(OrderListRow row, DomainScope scope)
    {
        OpenOrder = row;
        IsEditing = false;
        DetailTitle = $"{row.OrderNo}  ·  {row.Party}  ·  {row.OrderDate:dd-MMM-yyyy}  ·  {row.StatusText}";

        var lines =
            (from l in scope.Db.OrderLines.AsNoTracking()
             where l.OrderId == row.Id && l.QuantityOrdered > 0
             join i in scope.Db.Items on l.ItemId equals i.Id
             join g in scope.Db.Grades on l.GradeId equals g.Id
             join c in scope.Db.Colours on l.ColourId equals c.Id
             join b in scope.Db.Brands on l.BrandId equals b.Id
             orderby l.Id
             select new { l.Id, Item = i.Name, Grade = g.Name, Colour = c.Name, Brand = b.Name, l.QuantityOrdered, l.QuantityDispatched })
            .ToList();

        var lineIds = lines.Select(l => l.Id).ToList();
        var allocations =
            (from a in scope.Db.OrderLineAllocations.AsNoTracking()
             where lineIds.Contains(a.OrderLineId)
             join g in scope.Db.Grades on a.GradeId equals g.Id
             orderby a.Priority
             select new { a.OrderLineId, Grade = g.Name, a.BrandId, a.Bucket, a.Quantity, a.QuantityDispatched, a.QuantityReleased })
            .ToList();
        var brandNames = scope.Db.Brands.AsNoTracking().ToDictionary(b => b.Id, b => b.Name);

        Fill(DetailLines, lines.Select(l => new OrderLineDetailRow(
            l.Id, l.Item, l.Grade, l.Colour, l.Brand,
            l.QuantityOrdered, l.QuantityDispatched, l.QuantityOrdered - l.QuantityDispatched,
            string.Join(", ", allocations.Where(a => a.OrderLineId == l.Id).Select(a =>
            {
                var where = a.Bucket switch
                {
                    StockBucket.Packed => $"packed {(a.BrandId is int bid && brandNames.TryGetValue(bid, out var bn) ? bn : "?")}",
                    StockBucket.Raw => "unpacked (shared)",
                    _ => "not in stock"
                };
                var held = a.Quantity - a.QuantityDispatched - a.QuantityReleased;
                return $"{a.Quantity:0.###} from {a.Grade} {where}" + (held < a.Quantity ? $" (holding {held:0.###})" : "");
            })))).ToList());

        Fill(DetailAccessories,
            (from a in scope.Db.OrderAccessoryLines.AsNoTracking()
             where a.OrderId == row.Id && a.QuantityOrdered > 0
             join ac in scope.Db.Accessories on a.AccessoryId equals ac.Id
             orderby a.Id
             select new { ac.Name, a.SourceOrderLineId, a.QuantityOrdered, a.QuantityDispatched })
            .ToList()
            .Select(a => new OrderAccessoryDetailRow(
                a.Name,
                a.SourceOrderLineId is int p && lines.Any(l => l.Id == p)
                    ? lines.First(l => l.Id == p).Item
                    : "(added on its own)",
                a.QuantityOrdered, a.QuantityDispatched, a.QuantityOrdered - a.QuantityDispatched))
            .ToList());

        var dispatches =
            (from d in scope.Db.DispatchEntries.AsNoTracking()
             where d.OrderId == row.Id
             orderby d.Id descending
             select new
             {
                 d.Id, d.DispatchNo, d.Date, d.DispatchedBy, d.IsReversal,
                 Quantity = d.Lines.Sum(x => (decimal?)x.Quantity) ?? 0m,
                 IsReversed = scope.Db.DispatchEntries.Any(r => r.ReversesEntryId == d.Id)
             }).ToList();

        // "Still standing" = a real dispatch that has not been reversed. Only the newest of those
        // can be undone, which is the same order the service enforces.
        var latestLive = dispatches.FirstOrDefault(d => !d.IsReversal && !d.IsReversed);

        Fill(DetailDispatches, dispatches
            .Select(d => new OrderDispatchDetailRow(d.Id, d.DispatchNo, d.Date, d.DispatchedBy,
                d.Quantity, d.IsReversal, d.IsReversed, latestLive is not null && d.Id == latestLive.Id))
            .ToList());

        HasDetail = true;
    }

    private void ClearDetail()
    {
        OpenOrder = null;
        IsEditing = false;
        HasDetail = false;
        DetailTitle = "Select an order and press View.";
        DetailLines.Clear();
        DetailAccessories.Clear();
        DetailDispatches.Clear();
        EditLines.Clear();
    }

    // ---- Delete ---------------------------------------------------------------

    /// <summary>
    /// Deletes an order, which is <c>OrderService.Cancel</c>: the order stays on record as
    /// Cancelled with all its lines and allocations, and every reservation it still held goes back
    /// to the exact grade+brand rows it drew from. Anything already sent stays sent.
    /// </summary>
    [RelayCommand]
    private void Delete(OrderListRow? row)
    {
        row ??= OpenOrder;
        if (row is null) return;
        if (!row.CanDelete)
        {
            _dialogs.Error(row.Status == OrderStatus.Cancelled
                ? $"Order {row.OrderNo} has already been deleted."
                : $"Order {row.OrderNo} has been sent in full, so it cannot be deleted.");
            return;
        }
        if (!_dialogs.Confirm(
                $"Delete order {row.OrderNo} for {row.Party}?\n\n" +
                "The order stays on record as Deleted — nothing is erased — and any stock it was " +
                "holding becomes free for other orders. Anything already sent stays sent."))
            return;

        Run(scope =>
        {
            scope.Orders.Cancel(row.Id, "Deleted from the Orders screen");
            _dialogs.Info($"Order {row.OrderNo} deleted. Any stock it was holding is free again.");
        });
    }

    // ---- Reverse a dispatch ---------------------------------------------------

    /// <summary>
    /// Undoes a dispatch: the goods go back into the exact buckets and brand rows they left from,
    /// the order starts holding them again, and a linked reversing note is written. The original
    /// note stays on record.
    /// </summary>
    [RelayCommand]
    private void ReverseDispatch(OrderDispatchDetailRow? row)
    {
        if (row is null || OpenOrder is null) return;
        if (!row.CanReverse)
        {
            _dialogs.Error(row.IsReversal
                ? "This is a reversal note — there is nothing to undo."
                : row.IsReversed
                    ? $"Dispatch {row.DispatchNo} has already been undone."
                    : $"Dispatch {row.DispatchNo} cannot be undone while a later one still stands. " +
                      "Undo the most recent dispatch first.");
            return;
        }

        if (!_dialogs.Confirm(
                $"Undo dispatch {row.DispatchNo} ({row.Quantity:0.###})?\n\n" +
                "The goods go back into stock exactly where they came from, and the order holds " +
                "them again. The original note stays on record."))
            return;

        Run(scope =>
        {
            var reversal = scope.Dispatch.Reverse(row.Id);
            _dialogs.Info($"Dispatch {row.DispatchNo} undone ({reversal.DispatchNo}). " +
                          "The goods are back in stock and the order is holding them again.");
        });
    }

    // ---- Edit -----------------------------------------------------------------

    [RelayCommand]
    private void BeginEdit(OrderListRow? row)
    {
        row ??= OpenOrder;
        if (row is null) return;
        if (!row.CanEdit)
        {
            _dialogs.Error(row.Status == OrderStatus.Cancelled
                ? $"Order {row.OrderNo} has been deleted, so it cannot be edited."
                : $"Order {row.OrderNo} has already been sent, in whole or in part, so it can no " +
                  "longer be edited. Reverse the dispatch first, or book a new order for the difference.");
            return;
        }

        using (var scope = _scopes.Create())
        {
            if (OpenOrder?.Id != row.Id) OpenDetail(row, scope);

            var order = scope.Db.Orders.AsNoTracking().Single(o => o.Id == row.Id);
            EditParty = PartyOptions.FirstOrDefault(p => p.Id == order.PartyId);
            EditDate = order.OrderDate;
            EditRemarks = order.Remarks ?? string.Empty;

            var lines = scope.Db.OrderLines.AsNoTracking()
                .Where(l => l.OrderId == row.Id && l.QuantityOrdered > 0)
                .OrderBy(l => l.Id).ToList();

            EditLines.Clear();
            foreach (var l in lines)
                EditLines.Add(new OrderEditLineRow
                {
                    Item = Items.FirstOrDefault(x => x.Id == l.ItemId),
                    Grade = Grades.FirstOrDefault(x => x.Id == l.GradeId),
                    Colour = Colours.FirstOrDefault(x => x.Id == l.ColourId),
                    Brand = Brands.FirstOrDefault(x => x.Id == l.BrandId),
                    Quantity = l.QuantityOrdered.ToString("0.###")
                });
        }

        if (EditLines.Count == 0) AddEditLine();
        IsEditing = true;
    }

    [RelayCommand]
    private void AddEditLine() => EditLines.Add(new OrderEditLineRow());

    [RelayCommand]
    private void RemoveEditLine(OrderEditLineRow? row)
    {
        if (row is not null) EditLines.Remove(row);
        if (EditLines.Count == 0) AddEditLine();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        EditLines.Clear();
    }

    /// <summary>
    /// Saves the edited lines through <c>OrderService.Edit</c>, which releases the whole existing
    /// reservation and re-plans it from scratch under the same order number.
    /// </summary>
    [RelayCommand]
    private void SaveEdit()
    {
        if (OpenOrder is null) return;
        if (EditParty is null) { _dialogs.Error("Select a customer."); return; }

        var filled = EditLines.Where(l => l.Item is not null || !string.IsNullOrWhiteSpace(l.Quantity)).ToList();
        if (filled.Count == 0) { _dialogs.Error("An order needs at least one line."); return; }

        var lines = new List<OrderLineInput>();
        foreach (var row in filled)
        {
            if (row.Item is null || row.Grade is null || row.Colour is null || row.Brand is null)
            { _dialogs.Error("Every line needs an item, grade, colour and brand — or remove the line."); return; }
            if (!decimal.TryParse(row.Quantity, out var qty) || qty <= 0)
            { _dialogs.Error($"Enter a quantity greater than zero for {row.Item.Name}."); return; }
            lines.Add(new OrderLineInput(row.Item.Id, row.Grade.Id, row.Colour.Id, row.Brand.Id, qty));
        }

        var orderNo = OpenOrder.OrderNo;
        Run(scope =>
        {
            scope.Orders.Edit(OpenOrder.Id, new OrderInput(EditParty.Id, EditDate,
                string.IsNullOrWhiteSpace(EditRemarks) ? null : EditRemarks.Trim(),
                lines, Array.Empty<OrderAccessoryLineInput>()));
            IsEditing = false;
            EditLines.Clear();
            _dialogs.Info($"Order {orderNo} updated. Its reservation has been redone against current stock.");
        });
    }

    // ---- Plumbing -------------------------------------------------------------

    private void Run(Action<DomainScope> action)
    {
        try
        {
            using (var scope = _scopes.Create()) action(scope);
            Load();   // fresh scope, after the write is committed
        }
        catch (DomainException ex) { _dialogs.Error(ex.Message); }
        catch (Exception ex) { _dialogs.Error("Unexpected error: " + ex.Message); }
    }

    private static void Fill<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var x in source) target.Add(x);
    }
}
