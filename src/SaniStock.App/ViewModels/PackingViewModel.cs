using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaniStock.App.Infrastructure;
using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;

namespace SaniStock.App.ViewModels;

/// <summary>
/// A packing history row with the id needed to reverse it. Only packings that still stand appear
/// here — see <see cref="PackingViewModel.LoadHistory"/> for why the undone ones are left out.
/// </summary>
public record PackingHistoryRow(int Id, int BatchId, DateTime Date, string Item, string Grade,
    string Colour, string Brand, decimal Quantity, string CreatedBy);

/// <summary>
/// One brand line being staged on the packing form: how much of this packing goes to that brand.
/// The quantity stays a string until it is posted, like every other typed number in the app, so a
/// half-typed value never blows up the running total.
/// </summary>
public partial class PackingBrandRow : ObservableObject
{
    [ObservableProperty] private Brand? _brand;
    [ObservableProperty] private string _quantity = string.Empty;

    /// <summary>The typed quantity, or 0 when it is blank or not yet a valid number.</summary>
    public decimal ParsedQuantity =>
        decimal.TryParse(Quantity, out var q) && q > 0 ? q : 0m;
}

/// <summary>
/// Moves produced ware from "not packed" to "packed". Total stock never changes here — this only
/// records that goods have been packed and are ready to send.
/// <para>
/// Packing is where brand is decided, and one action can be split across several brands: a run of
/// 500 can go out as 200 / 200 / 100 for three brands in a single posting. The brand rows are
/// validated against the unpacked balance as a group, so the split can be rearranged freely as long
/// as it does not exceed what is waiting to be packed.
/// </para>
/// </summary>
public partial class PackingViewModel : ViewModelBase
{
    private readonly IDomainScopeFactory _scopes;
    private readonly IDialogService _dialogs;

    /// <summary>Unpacked quantity for the current selection, cached so the total line can judge it.</summary>
    private decimal _availableToPack;

    public PackingViewModel(IDomainScopeFactory scopes, IDialogService dialogs)
    {
        _scopes = scopes;
        _dialogs = dialogs;
        Title = "Packing";

        // Re-total whenever a row is added, removed, or its brand/quantity edited.
        BrandLines.CollectionChanged += OnBrandLinesChanged;
    }

    public ObservableCollection<Item> Items { get; } = new();
    public ObservableCollection<Grade> Grades { get; } = new();
    public ObservableCollection<Colour> Colours { get; } = new();
    public ObservableCollection<Brand> Brands { get; } = new();
    public ObservableCollection<PackingBrandRow> BrandLines { get; } = new();
    public ObservableCollection<PackingHistoryRow> History { get; } = new();

    [ObservableProperty] private Item? _packItem;
    [ObservableProperty] private Grade? _packGrade;
    [ObservableProperty] private Colour? _packColour;
    [ObservableProperty] private DateTime _packDate = DateTime.Today;
    [ObservableProperty] private string _packRemarks = string.Empty;

    /// <summary>Live "what is available to pack" hint for the chosen combination.</summary>
    [ObservableProperty] private string _selectionSummary = "Choose item, grade and colour to see what is waiting to be packed.";

    /// <summary>Running "total of N available" line under the brand rows.</summary>
    [ObservableProperty] private string _brandTotalSummary = string.Empty;

    /// <summary>True when the brand rows add up to more than is unpacked — the total line turns red.</summary>
    [ObservableProperty] private bool _isOverAllocated;

    public override void OnActivated() => Load();

    [RelayCommand]
    private void Load()
    {
        using var scope = _scopes.Create();
        Fill(Items, scope.Db.Items.Where(x => x.IsActive).OrderBy(x => x.Name).ToList());
        Fill(Grades, scope.Db.Grades.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToList());
        Fill(Colours, scope.Db.Colours.Where(x => x.IsActive).OrderBy(x => x.Name).ToList());
        Fill(Brands, scope.Db.Brands.Where(x => x.IsActive).OrderBy(x => x.Name).ToList());
        LoadHistory(scope);
        if (BrandLines.Count == 0) AddBrandLine();
        RefreshSummary();
    }

    /// <summary>
    /// Loads the packings that still stand. The table itself is append-only — undoing a packing
    /// appends a reversing entry and leaves the original on record — but this screen shows only what
    /// is currently packed, so both halves of an undone packing are filtered out: the reversal, and
    /// the entry it reverses. Otherwise an undone packing of 300 sits next to its own reversal of
    /// 300 and reads as the same packing posted twice. The full audit trail, reversals included, is
    /// on the Reports screen.
    /// </summary>
    private void LoadHistory(DomainScope scope)
    {
        var rows =
            (from e in scope.Db.PackingEntries
             join i in scope.Db.Items on e.ItemId equals i.Id
             join g in scope.Db.Grades on e.GradeId equals g.Id
             join c in scope.Db.Colours on e.ColourId equals c.Id
             join b in scope.Db.Brands on e.BrandId equals b.Id
             // "Has it been undone" is asked of the whole table, not of the page: the row that
             // reverses an entry always outranks it by id, but the page is capped and hiding the
             // original must not depend on its reversal landing inside that window.
             where !e.IsReversal && !scope.Db.PackingEntries.Any(r => r.ReversesEntryId == e.Id)
             orderby e.Id descending
             select new PackingHistoryRow(e.Id, e.BatchId ?? e.Id, e.Date, i.Name, g.Name, c.Name,
                 b.Name, e.Quantity, e.CreatedBy))
            .Take(100).ToList();
        History.Clear();
        foreach (var r in rows) History.Add(r);
    }

    // ---- Brand split ---------------------------------------------------------

    [RelayCommand]
    private void AddBrandLine()
    {
        var row = new PackingBrandRow();
        // Offer the first brand not already claimed, so a three-way split needs no extra clicks.
        row.Brand = Brands.FirstOrDefault(b => BrandLines.All(l => l.Brand?.Id != b.Id));
        BrandLines.Add(row);
    }

    [RelayCommand]
    private void RemoveBrandLine(PackingBrandRow? row)
    {
        if (row is null) return;
        BrandLines.Remove(row);
        // Never leave the form with nowhere to type; an empty row reads as "not packing anything".
        if (BrandLines.Count == 0) AddBrandLine();
    }

    private void OnBrandLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var row in e.OldItems?.Cast<PackingBrandRow>() ?? Enumerable.Empty<PackingBrandRow>())
            row.PropertyChanged -= OnBrandRowChanged;
        foreach (var row in e.NewItems?.Cast<PackingBrandRow>() ?? Enumerable.Empty<PackingBrandRow>())
            row.PropertyChanged += OnBrandRowChanged;
        RefreshBrandTotal();
    }

    private void OnBrandRowChanged(object? sender, PropertyChangedEventArgs e) => RefreshBrandTotal();

    private void RefreshBrandTotal()
    {
        var total = BrandLines.Sum(l => l.ParsedQuantity);
        IsOverAllocated = total > _availableToPack;
        BrandTotalSummary = $"Total: {total:0.###} of {_availableToPack:0.###} available" +
                            (IsOverAllocated ? "  —  more than is waiting to be packed" : string.Empty);
    }

    // Deferred, never inline: these fire from a binding write-back, including the one WPF performs
    // while tearing this screen down on navigation. Querying the database there runs inside the
    // layout pass. See ViewModelBase.RunAfterLayout.
    partial void OnPackItemChanged(Item? value) => RunAfterLayout(RefreshSummary);
    partial void OnPackGradeChanged(Grade? value) => RunAfterLayout(RefreshSummary);
    partial void OnPackColourChanged(Colour? value) => RunAfterLayout(RefreshSummary);

    private void RefreshSummary()
    {
        if (PackItem is null || PackGrade is null || PackColour is null)
        {
            _availableToPack = 0m;
            SelectionSummary = "Choose item, grade and colour to see what is waiting to be packed.";
            RefreshBrandTotal();
            return;
        }

        using var scope = _scopes.Create();
        // Unpacked stock lives on the brand-less row; packed is summed across every brand.
        var raw = scope.Stock.FindFinishedBalance(PackItem.Id, PackGrade.Id, PackColour.Id)?.RawOnHand ?? 0m;
        var packed = scope.Db.StockBalances
            .Where(b => b.ItemId == PackItem.Id && b.GradeId == PackGrade.Id && b.ColourId == PackColour.Id)
            .Sum(b => (decimal?)b.PackedOnHand) ?? 0m;

        _availableToPack = raw;
        SelectionSummary = raw > 0
            ? $"Waiting to be packed: {raw:0.###}   •   Already packed (all brands): {packed:0.###}"
            : $"Nothing waiting to be packed.   •   Already packed (all brands): {packed:0.###}";
        RefreshBrandTotal();
    }

    // ---- Posting -------------------------------------------------------------

    [RelayCommand]
    private void PostPacking()
    {
        if (PackItem is null || PackGrade is null || PackColour is null)
        { _dialogs.Error("Select item, grade and colour."); return; }

        var filled = BrandLines.Where(l => !string.IsNullOrWhiteSpace(l.Quantity) || l.Brand is not null).ToList();
        if (filled.Count == 0) { _dialogs.Error("Add at least one brand to pack under."); return; }

        var lines = new List<PackingBrandLine>();
        foreach (var row in filled)
        {
            if (row.Brand is null) { _dialogs.Error("Choose a brand on every line, or remove the line."); return; }
            if (!decimal.TryParse(row.Quantity, out var qty) || qty <= 0)
            { _dialogs.Error($"Enter a quantity greater than zero for {row.Brand.Name}."); return; }
            lines.Add(new PackingBrandLine(row.Brand.Id, qty));
        }
        if (lines.Select(l => l.BrandId).Distinct().Count() != lines.Count)
        { _dialogs.Error("The same brand is listed twice — combine those lines into one."); return; }

        var total = lines.Sum(l => l.Quantity);
        Run(scope =>
        {
            scope.Packing.Post(new PackingInput(PackDate, PackItem.Id, PackGrade.Id, PackColour.Id,
                lines, NullIfBlank(PackRemarks)));
            _dialogs.Info(lines.Count == 1
                ? $"Packed {total:0.###} {PackItem.UnitOfMeasure}."
                : $"Packed {total:0.###} {PackItem.UnitOfMeasure} across {lines.Count} brands.");
            BrandLines.Clear();
            AddBrandLine();
            PackRemarks = string.Empty;
        });
    }

    [RelayCommand]
    private void ReversePacking(PackingHistoryRow? row)
    {
        if (row is null) return;
        // Reversals and already-undone packings never reach this list, and PackingService.Reverse
        // refuses both anyway, so there is nothing to screen for here.
        if (!_dialogs.Confirm(
                $"Undo packing of {row.Quantity:0.###} — {row.Item} / {row.Grade} / {row.Colour} ({row.Brand})?\n\n" +
                "Other brands packed at the same time are not affected."))
            return;
        Run(scope => { scope.Packing.Reverse(row.Id); _dialogs.Info("Packing undone."); });
    }

    private void Run(Action<DomainScope> action)
    {
        try
        {
            using (var scope = _scopes.Create())
            {
                action(scope);
                LoadHistory(scope);
            }
            RefreshSummary(); // re-read the split on a fresh scope, after the write is committed
        }
        catch (DomainException ex) { _dialogs.Error(ex.Message); }
        catch (Exception ex) { _dialogs.Error("Unexpected error: " + ex.Message); }
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Fill<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var x in source) target.Add(x);
    }
}
