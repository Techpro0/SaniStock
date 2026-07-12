using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaniStock.App.Infrastructure;
using SaniStock.Data.Entities;
using SaniStock.Domain;

namespace SaniStock.App.ViewModels;

public record UserRow(int Id, string Username, string Role, bool IsActive);

public partial class UserManagementViewModel : ViewModelBase
{
    private readonly IDomainScopeFactory _scopes;
    private readonly IDialogService _dialogs;

    public UserManagementViewModel(IDomainScopeFactory scopes, IDialogService dialogs)
    {
        _scopes = scopes;
        _dialogs = dialogs;
        Title = "User Management";
    }

    public ObservableCollection<UserRow> Users { get; } = new();
    public UserRole[] Roles { get; } = { UserRole.Operator, UserRole.Admin };

    [ObservableProperty] private string _newUsername = string.Empty;
    [ObservableProperty] private UserRole _newRole = UserRole.Operator;
    [ObservableProperty] private UserRow? _selectedUser;

    public override void OnActivated() => Load();

    private void Load()
    {
        using var scope = _scopes.Create();
        Users.Clear();
        foreach (var u in scope.Db.Users.OrderBy(x => x.Username).ToList())
            Users.Add(new UserRow(u.Id, u.Username, u.Role.ToString(), u.IsActive));
    }

    /// <summary>Called from the view (PasswordBox is not bindable).</summary>
    public void CreateUser(string password)
    {
        try
        {
            using var scope = _scopes.Create();
            scope.Auth.CreateUser(NewUsername, password, NewRole);
            _dialogs.Info($"User '{NewUsername.Trim()}' created.");
            NewUsername = string.Empty;
            NewRole = UserRole.Operator;
            Load();
        }
        catch (DomainException ex) { _dialogs.Error(ex.Message); }
        catch (Exception ex) { _dialogs.Error("Unexpected error: " + ex.Message); }
    }

    public void ResetPassword(string password)
    {
        if (SelectedUser is null) { _dialogs.Error("Select a user first."); return; }
        try
        {
            using var scope = _scopes.Create();
            scope.Auth.ResetPassword(SelectedUser.Id, password);
            _dialogs.Info($"Password reset for '{SelectedUser.Username}'.");
        }
        catch (DomainException ex) { _dialogs.Error(ex.Message); }
        catch (Exception ex) { _dialogs.Error("Unexpected error: " + ex.Message); }
    }

    [RelayCommand]
    private void ToggleActive(UserRow? row)
    {
        if (row is null) return;
        try
        {
            using var scope = _scopes.Create();
            scope.Auth.SetActive(row.Id, !row.IsActive);
            Load();
        }
        catch (DomainException ex) { _dialogs.Error(ex.Message); }
        catch (Exception ex) { _dialogs.Error("Unexpected error: " + ex.Message); }
    }
}
