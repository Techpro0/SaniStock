using SaniStock.Data;
using SaniStock.Data.Entities;

namespace SaniStock.Domain.Services;

/// <summary>Login verification and user administration (BCrypt-hashed passwords).</summary>
public class AuthService
{
    private readonly SaniStockDbContext _db;
    private readonly IUserContext _user;

    public AuthService(SaniStockDbContext db, IUserContext user)
    {
        _db = db;
        _user = user;
    }

    /// <summary>Returns the user on valid credentials, or null. Inactive users cannot log in.</summary>
    public User? Authenticate(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return null;

        var user = _db.Users.FirstOrDefault(u => u.Username == username);
        if (user is null || !user.IsActive) return null;
        return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash) ? user : null;
    }

    public User CreateUser(string username, string password, UserRole role)
    {
        username = (username ?? string.Empty).Trim();
        if (username.Length == 0) throw new DomainException("Username is required.");
        if (string.IsNullOrEmpty(password) || password.Length < 4)
            throw new DomainException("Password must be at least 4 characters.");
        if (_db.Users.Any(u => u.Username == username))
            throw new DomainException($"A user named '{username}' already exists.");

        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
        _db.Users.Add(user);
        AddAudit("UserCreated", user.Username);
        _db.SaveChanges();
        return user;
    }

    public void ResetPassword(int userId, string newPassword)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 4)
            throw new DomainException("Password must be at least 4 characters.");
        var user = _db.Users.Find(userId) ?? throw new DomainException("User not found.");
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        AddAudit("PasswordReset", user.Username);
        _db.SaveChanges();
    }

    public void SetActive(int userId, bool isActive)
    {
        var user = _db.Users.Find(userId) ?? throw new DomainException("User not found.");
        if (!isActive && user.Role == UserRole.Admin
            && _db.Users.Count(u => u.Role == UserRole.Admin && u.IsActive) <= 1)
            throw new DomainException("Cannot deactivate the last active administrator.");
        user.IsActive = isActive;
        AddAudit(isActive ? "UserActivated" : "UserDeactivated", user.Username);
        _db.SaveChanges();
    }

    private void AddAudit(string action, string target)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTime.Now,
            Username = _user.Username,
            Action = action,
            EntityType = "User",
            Details = target
        });
    }
}
