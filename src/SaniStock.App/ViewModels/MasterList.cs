using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaniStock.App.Infrastructure;
using SaniStock.Data.Entities;
using SaniStock.Domain;

namespace SaniStock.App.ViewModels;

/// <summary>
/// Reusable list+edit-form controller for one master-data entity, so every tab (Item, Grade,
/// Colour, Brand, …) shares the same New / Edit / Save / Delete / Restore behaviour without
/// duplicated view-model code.
/// <para>
/// <b>Delete never removes anything.</b> It clears <see cref="IActivatable.IsActive"/> and saves
/// through the same validated path Edit uses, so the record and everything referring to it stay
/// intact and Restore puts it straight back. That is why the two commands are symmetrical and why
/// the grid can still show deleted rows.
/// </para>
/// </summary>
public partial class MasterList<T> : ObservableObject where T : class, IActivatable, new()
{
    private readonly Func<IEnumerable<T>> _load;
    private readonly Action<T> _save;
    private readonly Func<T, T> _clone;
    private readonly Func<T> _factory;
    private readonly IDialogService _dialogs;

    /// <summary>Singular, capitalised name of what this tab holds ("Colour"), used in prompts.</summary>
    private readonly string _label;

    /// <summary>Everything the load delegate returned, before the active/inactive filter.</summary>
    private List<T> _all = new();

    public ObservableCollection<T> Items { get; } = new();

    [ObservableProperty] private T _editing;

    /// <summary>
    /// Whether deleted (inactive) rows appear in the grid. On by default: hiding them would make a
    /// deletion look permanent and leave no obvious route to Restore.
    /// </summary>
    private bool _showInactive = true;
    public bool ShowInactive
    {
        get => _showInactive;
        set { if (SetProperty(ref _showInactive, value)) ApplyFilter(); }
    }

    public MasterList(Func<IEnumerable<T>> load, Action<T> save, Func<T, T> clone,
        Func<T> factory, IDialogService dialogs, string label)
    {
        _load = load;
        _save = save;
        _clone = clone;
        _factory = factory;
        _dialogs = dialogs;
        _label = label;
        _editing = factory();
    }

    public void Reload()
    {
        _all = _load().ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Items.Clear();
        foreach (var x in _all.Where(x => ShowInactive || x.IsActive)) Items.Add(x);
    }

    [RelayCommand]
    private void New() => Editing = _factory();

    [RelayCommand]
    private void Edit(T? item) { if (item is not null) Editing = _clone(item); }

    [RelayCommand]
    private void Save()
    {
        if (Persist(Editing)) Editing = _factory();
    }

    /// <summary>
    /// Switches a record off. The row stays in the database, and so does every stock balance,
    /// ledger movement and order line pointing at it — it simply stops being offered for new work.
    /// </summary>
    [RelayCommand]
    private void Delete(T? item)
    {
        if (item is null || !item.IsActive) return;
        if (!_dialogs.Confirm(
                $"Delete this {_label.ToLowerInvariant()}?\n\n" +
                "It will stop appearing in the lists you pick from, but nothing is erased: existing " +
                "stock, orders and history keep referring to it, and you can restore it here at any time."))
            return;

        SetActive(item, false);
    }

    /// <summary>Puts a deleted record back into use.</summary>
    [RelayCommand]
    private void Restore(T? item)
    {
        if (item is null || item.IsActive) return;
        SetActive(item, true);
    }

    /// <summary>
    /// Flips the flag on a copy and saves that, rather than mutating the grid's own instance —
    /// the same clone-then-save pattern Edit uses, so a rejected save leaves the grid untouched.
    /// </summary>
    private void SetActive(T item, bool isActive)
    {
        var copy = _clone(item);
        copy.IsActive = isActive;
        Persist(copy);
    }

    /// <summary>Saves through the entity's own validation, reporting failures. True when it stuck.</summary>
    private bool Persist(T entity)
    {
        try
        {
            _save(entity);
            Reload();
            return true;
        }
        catch (DomainException ex) { _dialogs.Error(ex.Message); }
        catch (Exception ex) { _dialogs.Error("Unexpected error: " + ex.Message); }
        return false;
    }
}
