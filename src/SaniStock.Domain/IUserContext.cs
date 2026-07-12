using SaniStock.Data.Entities;

namespace SaniStock.Domain;

/// <summary>
/// Identifies who is currently acting, so services can stamp CreatedBy / AuditLog rows
/// without threading a username through every call.
/// </summary>
public interface IUserContext
{
    string Username { get; }
    UserRole Role { get; }
    bool IsAuthenticated { get; }
}

/// <summary>Simple mutable implementation set by the app after login.</summary>
public class UserContext : IUserContext
{
    public string Username { get; set; } = "system";
    public UserRole Role { get; set; } = UserRole.Operator;
    public bool IsAuthenticated { get; set; }

    public void SignIn(User user)
    {
        Username = user.Username;
        Role = user.Role;
        IsAuthenticated = true;
    }

    public void SignOut()
    {
        Username = "system";
        Role = UserRole.Operator;
        IsAuthenticated = false;
    }
}
