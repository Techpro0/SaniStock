using CommunityToolkit.Mvvm.ComponentModel;
using SaniStock.App.Infrastructure;
using SaniStock.Domain;

namespace SaniStock.App.ViewModels;

/// <summary>Backs the login window. Password is supplied by the view (PasswordBox is not bindable).</summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly IDomainScopeFactory _scopes;
    private readonly UserContext _user;

    [ObservableProperty] private string _username = "admin";
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    public LoginViewModel(IDomainScopeFactory scopes, UserContext user)
    {
        _scopes = scopes;
        _user = user;
    }

    /// <summary>Returns true and populates the shared user context on success.</summary>
    public bool TryLogin(string password)
    {
        HasError = false;
        ErrorMessage = string.Empty;
        try
        {
            using var scope = _scopes.Create();
            var user = scope.Auth.Authenticate(Username?.Trim() ?? string.Empty, password);
            if (user is null)
            {
                HasError = true;
                ErrorMessage = "Invalid username or password, or the account is inactive.";
                return false;
            }
            _user.SignIn(user);
            return true;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = "Login failed: " + ex.Message;
            return false;
        }
    }
}
