using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SaniStock.Domain;

namespace SaniStock.App.ViewModels;

/// <summary>One sidebar navigation entry.</summary>
public record NavItem(string Icon, string Title, Type ViewModelType, bool AdminOnly);

public partial class ShellViewModel : ObservableObject
{
    private readonly IServiceProvider _sp;
    private readonly UserContext _user;

    public ObservableCollection<NavItem> NavItems { get; } = new();

    [ObservableProperty] private ViewModelBase? _current;
    [ObservableProperty] private NavItem? _selected;

    public string UserName => _user.Username;
    public string UserRole => _user.Role.ToString();

    /// <summary>Raised when the user chooses to sign out; the app handles returning to login.</summary>
    public event Action? LogoutRequested;

    public ShellViewModel(IServiceProvider sp, UserContext user)
    {
        _sp = sp;
        _user = user;

        var all = new[]
        {
            new NavItem("🏠", "Home", typeof(DashboardViewModel), false),
            new NavItem("➕", "Add Stock", typeof(ProductionViewModel), false),
            new NavItem("📦", "Stock", typeof(StockViewModel), false),
            new NavItem("📝", "New Order", typeof(OrderBookingViewModel), false),
            new NavItem("🚚", "Send Order", typeof(OrderDispatchViewModel), false),
            new NavItem("📊", "Reports", typeof(ReportsViewModel), false),
            new NavItem("🗂️", "Setup Lists", typeof(MasterDataViewModel), true),
            new NavItem("👤", "Users", typeof(UserManagementViewModel), true),
            new NavItem("💾", "Backup", typeof(BackupViewModel), true),
            new NavItem("ℹ️", "About", typeof(AboutViewModel), false),
        };

        var isAdmin = _user.Role == Data.Entities.UserRole.Admin;
        foreach (var n in all.Where(n => isAdmin || !n.AdminOnly))
            NavItems.Add(n);

        Selected = NavItems.FirstOrDefault();
    }

    partial void OnSelectedChanged(NavItem? value)
    {
        if (value is null) return;
        var vm = (ViewModelBase)_sp.GetRequiredService(value.ViewModelType);
        Current = vm;
        vm.OnActivated();
    }

    [RelayCommand]
    private void Logout() => LogoutRequested?.Invoke();
}
