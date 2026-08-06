using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SaniStock.Data;
using SaniStock.Data.Entities;
using Xunit;

namespace SaniStock.Domain.Tests;

/// <summary>
/// Locks in <see cref="DbSeeder"/>'s core promise: it only ever adds rows that do not already
/// exist, and never edits or reverts one a client has since customized. This is the guarantee the
/// next release depends on to avoid clobbering a client's edited product types, grades, brands or
/// admin account — <see cref="DbSeeder.EnsureSeeded"/> runs at <em>every</em> startup, not just
/// the first, so this has to hold indefinitely, not just on a fresh database.
/// </summary>
public class DbSeederTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SaniStockDbContext _db;

    public DbSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new SaniStockDbContext(
            new DbContextOptionsBuilder<SaniStockDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void Seeding_twice_does_not_duplicate_any_row()
    {
        DbSeeder.EnsureSeeded(_db);
        var grades = _db.Grades.Count();
        var productTypes = _db.ProductTypes.Count();
        var brands = _db.Brands.Count();
        var users = _db.Users.Count();

        DbSeeder.EnsureSeeded(_db);

        Assert.Equal(grades, _db.Grades.Count());
        Assert.Equal(productTypes, _db.ProductTypes.Count());
        Assert.Equal(brands, _db.Brands.Count());
        Assert.Equal(users, _db.Users.Count());
    }

    [Fact]
    public void Reseeding_does_not_revert_a_renamed_product_type()
    {
        DbSeeder.EnsureSeeded(_db);
        var wc = _db.ProductTypes.Single(p => p.Code == "PT-WC");
        wc.Name = "European Closet";   // the client's own naming
        _db.SaveChanges();

        DbSeeder.EnsureSeeded(_db);

        Assert.Equal("European Closet", _db.ProductTypes.Single(p => p.Code == "PT-WC").Name);
    }

    [Fact]
    public void Reseeding_does_not_reactivate_a_product_type_the_client_deactivated()
    {
        DbSeeder.EnsureSeeded(_db);
        var urinal = _db.ProductTypes.Single(p => p.Code == "PT-URN");
        urinal.IsActive = false;
        _db.SaveChanges();

        DbSeeder.EnsureSeeded(_db);

        Assert.False(_db.ProductTypes.Single(p => p.Code == "PT-URN").IsActive);
    }

    [Fact]
    public void Reseeding_does_not_touch_a_renamed_or_reordered_grade()
    {
        DbSeeder.EnsureSeeded(_db);
        var first = _db.Grades.Single(g => g.Name == "1st");
        first.Name = "Premium";
        first.SortOrder = 5;
        _db.SaveChanges();

        DbSeeder.EnsureSeeded(_db);

        var reloaded = _db.Grades.Single(g => g.Id == first.Id);
        Assert.Equal("Premium", reloaded.Name);
        Assert.Equal(5, reloaded.SortOrder);
        // And a fresh "1st" was not inserted alongside it under the seeded name.
        Assert.DoesNotContain(_db.Grades, g => g.Name == "1st");
    }

    [Fact]
    public void Reseeding_does_not_reactivate_the_default_brand_once_the_client_deactivated_it()
    {
        DbSeeder.EnsureSeeded(_db);
        var brand = _db.Brands.Single(b => b.Code == Brand.DefaultCode);
        brand.IsActive = false;
        brand.Name = "Legacy";
        _db.SaveChanges();

        DbSeeder.EnsureSeeded(_db);

        // Exactly one brand still — a rename/recode must not make the seeder think the starter
        // row is missing and insert a phantom second Unbranded alongside it.
        var reloaded = Assert.Single(_db.Brands);
        Assert.False(reloaded.IsActive);
        Assert.Equal("Legacy", reloaded.Name);
    }

    [Fact]
    public void Reseeding_never_creates_a_second_admin_once_any_user_exists()
    {
        // A client that renamed the default admin, or created their own and deactivated the
        // original, still has "a user" — DbSeeder must not slip a fresh admin/admin123 back in.
        _db.Users.Add(new User
        {
            Username = "priya",
            PasswordHash = "irrelevant-for-this-test",
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.Now
        });
        _db.SaveChanges();

        DbSeeder.EnsureSeeded(_db);

        Assert.Single(_db.Users);
        Assert.DoesNotContain(_db.Users, u => u.Username == DbSeeder.DefaultAdminUsername);
    }
}
