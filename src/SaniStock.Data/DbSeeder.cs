using SaniStock.Data.Entities;

namespace SaniStock.Data;

/// <summary>Seeds baseline reference data and a default admin login on first run.</summary>
public static class DbSeeder
{
    public const string DefaultAdminUsername = "admin";
    public const string DefaultAdminPassword = "admin123";

    /// <summary>Idempotent — safe to call at every startup after migrations are applied.</summary>
    public static void EnsureSeeded(SaniStockDbContext db)
    {
        SeedGrades(db);
        SeedAdmin(db);
        db.SaveChanges();
    }

    private static void SeedGrades(SaniStockDbContext db)
    {
        var wanted = new[]
        {
            ("1st", 1),
            ("2nd", 2),
            ("3rd", 3),
        };
        foreach (var (name, sort) in wanted)
        {
            if (!db.Grades.Any(g => g.Name == name))
                db.Grades.Add(new Grade { Name = name, SortOrder = sort, IsActive = true });
        }
    }

    private static void SeedAdmin(SaniStockDbContext db)
    {
        if (db.Users.Any()) return;
        db.Users.Add(new User
        {
            Username = DefaultAdminUsername,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultAdminPassword),
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.Now
        });
    }
}
