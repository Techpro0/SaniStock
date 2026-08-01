using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SaniStock.App.ViewModels;

/// <summary>Base for all view models: exposes a title and a busy flag.</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Called when the view becomes active, to (re)load data.</summary>
    public virtual void OnActivated() { }

    /// <summary>
    /// Runs <paramref name="work"/> after the current layout pass instead of immediately.
    /// <para>
    /// Use this for anything a <c>SelectedItem</c> setter triggers that queries the database or
    /// replaces an <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>. WPF also
    /// resets <c>SelectedItem</c> while a screen is being torn down on navigation — the inherited
    /// DataContext is invalidated, every <c>ItemsSource</c> binding re-transfers as null, and each
    /// Selector clears its selection. Reloading synchronously at that moment mutates collections
    /// the framework is already walking, which is how a routine "pick a filter" turns into a
    /// NullReferenceException deep inside WPF's binding engine.
    /// </para>
    /// Deferring costs nothing perceptible and means the reload either happens on a settled visual
    /// tree or is harmlessly discarded with the dead view model.
    /// </summary>
    protected static void RunAfterLayout(Action work)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) { work(); return; }   // no UI (tests) — just run it
        dispatcher.BeginInvoke(work, DispatcherPriority.Background);
    }
}
