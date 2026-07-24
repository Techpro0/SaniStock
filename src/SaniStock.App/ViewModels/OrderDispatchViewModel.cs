using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaniStock.App.Infrastructure;
using SaniStock.Domain;
using SaniStock.Domain.Models;

namespace SaniStock.App.ViewModels;

/// <summary>An open order shown in the dispatch picker.</summary>
public record OpenOrderRow(int Id, string OrderNo, DateTime OrderDate, string Party, string Status);

/// <summary>An editable finished dispatch line (quantity the user intends to ship now).</summary>
public partial class DispatchLineRow : ObservableObject
{
    public int OrderLineId { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal Ordered { get; init; }
    public decimal Dispatched { get; init; }
    public decimal Pending { get; init; }
    [ObservableProperty] private string _dispatchQty = string.Empty;

    /// <summary>Bundled accessory lines that were booked alongside this item line.</summary>
    public ObservableCollection<DispatchAccessoryRow> Accessories { get; } = new();
    public bool HasAccessories => Accessories.Count > 0;
}

/// <summary>An editable accessory dispatch line.</summary>
public partial class DispatchAccessoryRow : ObservableObject
{
    public int OrderAccessoryLineId { get; init; }
    public int? SourceOrderLineId { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal Ordered { get; init; }
    public decimal Dispatched { get; init; }
    public decimal Pending { get; init; }
    [ObservableProperty] private string _dispatchQty = string.Empty;
}

public partial class OrderDispatchViewModel : ViewModelBase
{
    private readonly IDomainScopeFactory _scopes;
    private readonly IDialogService _dialogs;

    public OrderDispatchViewModel(IDomainScopeFactory scopes, IDialogService dialogs)
    {
        _scopes = scopes;
        _dialogs = dialogs;
        Title = "Order Dispatch";
    }

    public ObservableCollection<OpenOrderRow> OpenOrders { get; } = new();
    public ObservableCollection<DispatchLineRow> Lines { get; } = new();
    /// <summary>Standalone accessory lines not tied to any item line (manually added at booking).</summary>
    public ObservableCollection<DispatchAccessoryRow> AccessoryLines { get; } = new();

    [ObservableProperty] private OpenOrderRow? _selectedOrder;
    [ObservableProperty] private DateTime _dispatchDate = DateTime.Today;
    [ObservableProperty] private string _remarks = string.Empty;

    partial void OnSelectedOrderChanged(OpenOrderRow? value) => LoadLines(value);

    public override void OnActivated() => Load();

    [RelayCommand]
    private void Load()
    {
        using var scope = _scopes.Create();
        var rows =
            (from o in scope.Db.Orders
             join p in scope.Db.Parties on o.PartyId equals p.Id
             where o.Status == Data.Entities.OrderStatus.Booked || o.Status == Data.Entities.OrderStatus.PartiallyDispatched
             orderby o.OrderDate
             select new OpenOrderRow(o.Id, o.OrderNo, o.OrderDate, p.Name, o.Status.ToString()))
            .ToList();
        OpenOrders.Clear();
        foreach (var r in rows) OpenOrders.Add(r);
        Lines.Clear();
        AccessoryLines.Clear();
    }

    private void LoadLines(OpenOrderRow? order)
    {
        Lines.Clear();
        AccessoryLines.Clear();
        if (order is null) return;

        using var scope = _scopes.Create();
        var lines =
            from l in scope.Db.OrderLines
            where l.OrderId == order.Id
            join i in scope.Db.Items on l.ItemId equals i.Id
            join g in scope.Db.Grades on l.GradeId equals g.Id
            join c in scope.Db.Colours on l.ColourId equals c.Id
            select new { l.Id, Desc = i.Name + " / " + g.Name + " / " + c.Name, l.QuantityOrdered, l.QuantityDispatched };
        var byLineId = new Dictionary<int, DispatchLineRow>();
        foreach (var l in lines.ToList())
        {
            var row = new DispatchLineRow
            {
                OrderLineId = l.Id,
                Description = l.Desc,
                Ordered = l.QuantityOrdered,
                Dispatched = l.QuantityDispatched,
                Pending = l.QuantityOrdered - l.QuantityDispatched
            };
            Lines.Add(row);
            byLineId[l.Id] = row;
        }

        var accs =
            from l in scope.Db.OrderAccessoryLines
            where l.OrderId == order.Id
            join a in scope.Db.Accessories on l.AccessoryId equals a.Id
            select new { l.Id, l.SourceOrderLineId, a.Name, l.QuantityOrdered, l.QuantityDispatched };
        foreach (var a in accs.ToList())
        {
            var accRow = new DispatchAccessoryRow
            {
                OrderAccessoryLineId = a.Id,
                SourceOrderLineId = a.SourceOrderLineId,
                Description = a.Name,
                Ordered = a.QuantityOrdered,
                Dispatched = a.QuantityDispatched,
                Pending = a.QuantityOrdered - a.QuantityDispatched
            };
            // Nest under the parent item line if it belongs to one; else it's a standalone accessory.
            if (a.SourceOrderLineId is int parentId && byLineId.TryGetValue(parentId, out var parent))
                parent.Accessories.Add(accRow);
            else
                AccessoryLines.Add(accRow);
        }
    }

    [RelayCommand]
    private void DispatchAllPending()
    {
        foreach (var l in Lines)
        {
            l.DispatchQty = l.Pending > 0 ? l.Pending.ToString("0.###") : string.Empty;
            foreach (var a in l.Accessories) a.DispatchQty = a.Pending > 0 ? a.Pending.ToString("0.###") : string.Empty;
        }
        foreach (var a in AccessoryLines) a.DispatchQty = a.Pending > 0 ? a.Pending.ToString("0.###") : string.Empty;
    }

    [RelayCommand]
    private void Dispatch()
    {
        if (SelectedOrder is null) { _dialogs.Error("Select an order."); return; }

        var lineInputs = new List<DispatchLineInput>();
        foreach (var l in Lines)
        {
            if (string.IsNullOrWhiteSpace(l.DispatchQty)) continue;
            if (!decimal.TryParse(l.DispatchQty, out var q) || q < 0) { _dialogs.Error($"Invalid quantity for {l.Description}."); return; }
            if (q > 0) lineInputs.Add(new DispatchLineInput(l.OrderLineId, q));
        }
        var accInputs = new List<DispatchAccessoryLineInput>();
        // Bundled accessories nested under item lines, then standalone accessory lines.
        var allAccessoryRows = Lines.SelectMany(l => l.Accessories).Concat(AccessoryLines);
        foreach (var a in allAccessoryRows)
        {
            if (string.IsNullOrWhiteSpace(a.DispatchQty)) continue;
            if (!decimal.TryParse(a.DispatchQty, out var q) || q < 0) { _dialogs.Error($"Invalid quantity for {a.Description}."); return; }
            if (q > 0) accInputs.Add(new DispatchAccessoryLineInput(a.OrderAccessoryLineId, q));
        }
        if (lineInputs.Count == 0 && accInputs.Count == 0) { _dialogs.Error("Enter at least one dispatch quantity."); return; }

        try
        {
            using var scope = _scopes.Create();
            var d = scope.Dispatch.Dispatch(new DispatchInput(SelectedOrder.Id, DispatchDate,
                string.IsNullOrWhiteSpace(Remarks) ? null : Remarks.Trim(), lineInputs, accInputs));
            _dialogs.Info($"Dispatch {d.DispatchNo} posted.");
            Remarks = string.Empty;
            Load();
        }
        catch (DomainException ex) { _dialogs.Error(ex.Message); }
        catch (Exception ex) { _dialogs.Error("Unexpected error: " + ex.Message); }
    }
}
