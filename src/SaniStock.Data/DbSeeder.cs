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
    /// One starter brand, so packing works on a brand-new database without a trip to Setup Lists,
    /// using the same code the Brand migration assigns to pre-existing packed stock — so an
    /// upgraded database and a fresh one both end up with exactly this one row rather than two
    /// near-identical brands.
    /// <para>
    /// Seeded once, on a genuinely empty table, never topped up again — unlike <see cref="SeedProductTypes"/>,
    /// a brand has no field an admin can't rename, so matching on "does this code already exist"
    /// would insert a phantom second Unbranded the moment the admin recodes or renames the first
    /// one. "The table already has a brand" is the check that stays true no matter what an admin
    /// has since done to the row, exactly like <see cref="SeedAdmin"/>.
    /// </para>
    /// </summary>
    private static void SeedBrands(SaniStockDbContext db)
    {
        if (db.Brands.Any()) return;
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

    /// <summary>
    /// Starter grades, seeded once on a genuinely empty table and never topped up again. Unlike
    /// <see cref="SeedProductTypes"/>, a grade has no separate code an admin's rename can't touch —
    /// matching on name would insert a phantom "1st" back in the moment an admin renames it to
    /// anything else, silently corrupting the grade-priority chain (<c>ResolveGradeChain</c> picks
    /// the two lowest <c>SortOrder</c> among <em>active</em> grades, so a stray extra one joins it).
    /// "The table already has a grade" is the check that survives any rename, exactly like
    /// <see cref="SeedAdmin"/>.
    /// </summary>
    private static void SeedGrades(SaniStockDbContext db)
    {
        if (db.Grades.Any()) return;

        db.Grades.Add(new Grade { Name = "1st", SortOrder = 1, IsActive = true });
        db.Grades.Add(new Grade { Name = "2nd", SortOrder = 2, IsActive = true });
        db.Grades.Add(new Grade { Name = "3rd", SortOrder = 3, IsActive = true });
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
