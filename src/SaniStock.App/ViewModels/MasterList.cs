using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaniStock.Domain;

namespace SaniStock.App.ViewModels;

/// <summary>
/// Reusable list+edit-form controller for one master-data entity, so every tab (Item, Grade,
/// Colour, …) shares the same New / Edit / Save behaviour without duplicated view-model code.
/// </summary>
public partial class MasterList<T> : ObservableObject where T : class, new()
{
    private readonly Func<IEnumerable<T>> _load;
    private readonly Action<T> _save;
    private readonly Func<T, T> _clone;
    private readonly Func<T> _factory;
    private readonly Action<string> _onError;

    public ObservableCollection<T> Items { get; } = new();

    [ObservableProperty] private T _editing;

    public MasterList(Func<IEnumerable<T>> load, Action<T> save, Func<T, T> clone,
        Func<T> factory, Action<string> onError)
    {
        _load = load;
        _save = save;
        _clone = clone;
        _factory = factory;
        _onError = onError;
        _editing = factory();
    }

    public void Reload()
    {
        Items.Clear();
        foreach (var x in _load()) Items.Add(x);
    }

    [RelayCommand]
    private void New() => Editing = _factory();

    [RelayCommand]
    private void Edit(T? item) { if (item is not null) Editing = _clone(item); }

    [RelayCommand]
    private void Save()
    {
        try
        {
            _save(Editing);
            Reload();
            Editing = _factory();
        }
        catch (DomainException ex) { _onError(ex.Message); }
        catch (Exception ex) { _onError("Unexpected error: " + ex.Message); }
    }
}
