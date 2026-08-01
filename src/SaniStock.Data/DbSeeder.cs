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
        SeedProductTypes(db);
        SeedBrands(db);
        SeedAdmin(db);
        db.SaveChanges();
    }

    /// <summary>
    /// One starter brand, so packing works on a brand-new database without a trip to Setup Lists.
    /// Seeded by code, like product types, and deliberately the same code the Brand migration
    /// assigns to pre-existing packed stock — so an upgraded database and a fresh one both end up
    /// with exactly this one row rather than two near-identical brands.
    /// </summary>
    private static void SeedBrands(SaniStockDbContext db)
    {
        if (!db.Brands.Any(x => x.Code == Brand.DefaultCode))
            db.Brands.Add(new Brand { Code = Brand.DefaultCode, Name = "Unbranded", IsActive = true });
    }

    /// <summary>
    /// Standard sanitary-ware product types. This is a starter set only — admins add, edit
    /// or deactivate types from Setup Lists → Product Type, so we seed each by code just once
    /// and never overwrite an existing row's edits.
    /// </summary>
    private static void SeedProductTypes(SaniStockDbContext db)
    {
        var wanted = new (string Code, string Name)[]
        {
            ("PT-WC",   "Water Closet"),
            ("PT-WB",   "Wash Basin"),
            ("PT-URN",  "Urinal"),
            ("PT-BID",  "Bidet"),
            ("PT-BTUB", "Bathtub"),
            ("PT-CIST", "Cistern"),
            ("PT-STRY", "Shower Tray"),
            ("PT-BMIX", "Basin Mixer"),
            ("PT-BIBC", "Bib Cock"),
            ("PT-PILC", "Pillar Cock"),
            ("PT-ANGV", "Angle Valve"),
            ("PT-STPC", "Stop Cock"),
            ("PT-WLMX", "Wall Mixer"),
            ("PT-BTMX", "Bath Mixer"),
            ("PT-KTMX", "Kitchen Mixer"),
            ("PT-SHMX", "Shower Mixer"),
            ("PT-SNSF", "Sensor Faucet"),
        };
        foreach (var (code, name) in wanted)
        {
            if (!db.ProductTypes.Any(p => p.Code == code))
                db.ProductTypes.Add(new ProductType { Code = code, Name = name, IsActive = true });
        }
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
