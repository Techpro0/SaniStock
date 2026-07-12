using CommunityToolkit.Mvvm.ComponentModel;

namespace SaniStock.App.ViewModels;

/// <summary>Base for all view models: exposes a title and a busy flag.</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Called when the view becomes active, to (re)load data.</summary>
    public virtual void OnActivated() { }
}
