using Microsoft.EntityFrameworkCore;
using SaniStock.Data;
using SaniStock.Data.Entities;

namespace SaniStock.Domain.Services;

/// <summary>Validated create/update for master data (unique codes/names, required fields).</summary>
public class MasterDataService
{
    private readonly SaniStockDbContext _db;

    public MasterDataService(SaniStockDbContext db) => _db = db;

    // ---- Item ----
    public Item SaveItem(Item item)
    {
        item.Code = string.IsNullOrWhiteSpace(item.Code)
            ? GenerateNextCode("IT-", _db.Items.Select(x => x.Code).ToList())
            : Require(item.Code, "Item code");
        item.Name = Require(item.Name, "Item name");
        if (_db.Items.Any(x => x.Code == item.Code && x.Id != item.Id))
            throw new DomainException($"Item code '{item.Code}' already exists.");
        // The UI uses a 0-id "(None)" sentinel for "no product type"; store it as null.
        if (item.ProductTypeId is 0) item.ProductTypeId = null;
        if (item.ProductTypeId is int ptId && !_db.ProductTypes.Any(p => p.Id == ptId))
            throw new DomainException("Selected product type does not exist.");
        Upsert(_db.Items, item);
        _db.SaveChanges();
        return item;
    }

    // ---- ProductType ----
    public ProductType SaveProductType(ProductType p)
    {
        p.Code = string.IsNullOrWhiteSpace(p.Code)
            ? GenerateNextCode("PT-", _db.ProductTypes.Select(x => x.Code).ToList())
            : Require(p.Code, "Product type code");
        p.Name = Require(p.Name, "Product type name");
        if (_db.ProductTypes.Any(x => x.Code == p.Code && x.Id != p.Id))
            throw new DomainException($"Product type code '{p.Code}' already exists.");
        Upsert(_db.ProductTypes, p);
        _db.SaveChanges();
        return p;
    }

    // ---- Accessory ----
    public Accessory SaveAccessory(Accessory a)
    {
        a.Code = string.IsNullOrWhiteSpace(a.Code)
            ? GenerateNextCode("AC-", _db.Accessories.Select(x => x.Code).ToList())
            : Require(a.Code, "Accessory code");
        a.Name = Require(a.Name, "Accessory name");
        if (_db.Accessories.Any(x => x.Code == a.Code && x.Id != a.Id))
            throw new DomainException($"Accessory code '{a.Code}' already exists.");
        Upsert(_db.Accessories, a);
        _db.SaveChanges();
        return a;
    }

    // ---- Colour ----
    public Colour SaveColour(Colour c)
    {
        c.Name = Require(c.Name, "Colour name");
        if (_db.Colours.Any(x => x.Name == c.Name && x.Id != c.Id))
            throw new DomainException($"Colour '{c.Name}' already exists.");
        Upsert(_db.Colours, c);
        _db.SaveChanges();
        return c;
    }

    // ---- Brand ----
    public Brand SaveBrand(Brand b)
    {
        b.Code = string.IsNullOrWhiteSpace(b.Code)
            ? GenerateNextCode("BR-", _db.Brands.Select(x => x.Code).ToList())
            : Require(b.Code, "Brand code");
        b.Name = Require(b.Name, "Brand name");
        if (_db.Brands.Any(x => x.Code == b.Code && x.Id != b.Id))
            throw new DomainException($"Brand code '{b.Code}' already exists.");
        Upsert(_db.Brands, b);
        _db.SaveChanges();
        return b;
    }

    // ---- Grade ----

    /// <summary>
    /// Saves a grade, refusing to switch off the last active one.
    /// <para>
    /// Grades are load-bearing in a way the other lists are not: production, packing and every
    /// order line must name one, and <c>StockAllocationService.ResolveGradeChain</c> derives the
    /// cross-grade borrowing chain from the active grades' sort order. With none left, no stock
    /// could be posted at all — so this mirrors the "last active administrator" rule in
    /// <see cref="AuthService"/>.
    /// </para>
    /// Deleting a grade while others remain is allowed, but note it silently re-points the
    /// borrowing chain, since that chain is always "the two lowest sort orders still active".
    /// </summary>
    public Grade SaveGrade(Grade g)
    {
        g.Name = Require(g.Name, "Grade name");
        if (_db.Grades.Any(x => x.Name == g.Name && x.Id != g.Id))
            throw new DomainException($"Grade '{g.Name}' already exists.");

        if (!g.IsActive && !_db.Grades.Any(x => x.IsActive && x.Id != g.Id))
            throw new DomainException(
                "This is the last active grade, so it cannot be deleted — every item of stock and " +
                "every order line has to have a grade. Add another grade first.");

        Upsert(_db.Grades, g);
        _db.SaveChanges();
        return g;
    }

    // ---- Party ----
    public Party SaveParty(Party p)
    {
        p.Name = Require(p.Name, "Party name");
        Upsert(_db.Parties, p);
        _db.SaveChanges();
        return p;
    }

    // ---- RawMaterial ----
    public RawMaterial SaveRawMaterial(RawMaterial r)
    {
        r.Name = Require(r.Name, "Raw material name");
        if (_db.RawMaterials.Any(x => x.Name == r.Name && x.Id != r.Id))
            throw new DomainException($"Raw material '{r.Name}' already exists.");
        Upsert(_db.RawMaterials, r);
        _db.SaveChanges();
        return r;
    }

    // ---- Item accessory defaults (bundling recipe) ----

    /// <summary>
    /// The item's current recipe — the <em>active</em> rows only, accessory name/code included.
    /// Rows dropped from the recipe are deactivated rather than removed, so they are deliberately
    /// not returned here: to every caller the recipe reads exactly as the user last left it.
    /// </summary>
    public List<ItemAccessoryDefault> GetItemAccessoryDefaults(int itemId) =>
        _db.ItemAccessoryDefaults
            .Include(x => x.Accessory)
            .Where(x => x.ItemId == itemId && x.IsActive)
            .OrderBy(x => x.Accessory!.Name)
            .ToList();

    /// <summary>
    /// Replaces an item's default-accessory recipe with the supplied set. Each pair must have a
    /// positive quantity and reference an existing accessory; duplicates are rejected. Passing an
    /// empty set clears the recipe (equivalent to "don't include accessories by default").
    /// <para>
    /// Dropped rows are <b>deactivated, not removed</b> — nothing in this application deletes a
    /// row. Reconciling rather than replacing also means an accessory taken out of a recipe and
    /// later put back reuses its original row, which matters because
    /// <c>(ItemId, AccessoryId)</c> is a unique pair: a lingering deactivated row would otherwise
    /// collide with a fresh insert.
    /// </para>
    /// </summary>
    public void SetItemAccessoryDefaults(int itemId, IReadOnlyList<(int AccessoryId, decimal QtyPerUnit)> defaults)
    {
        if (!_db.Items.Any(i => i.Id == itemId))
            throw new DomainException("Item does not exist.");

        var seen = new HashSet<int>();
        foreach (var (accessoryId, qty) in defaults)
        {
            if (!seen.Add(accessoryId))
                throw new DomainException("The same accessory is listed twice in the defaults.");
            if (qty <= 0)
                throw new DomainException("Quantity per unit must be greater than zero.");
            if (!_db.Accessories.Any(a => a.Id == accessoryId))
                throw new DomainException("A selected accessory does not exist.");
        }

        // Every row for this item, active or not — a deactivated one is what gets revived when an
        // accessory is added back to the recipe.
        var existing = _db.ItemAccessoryDefaults.Where(x => x.ItemId == itemId).ToList();

        foreach (var (accessoryId, qty) in defaults)
        {
            var row = existing.FirstOrDefault(x => x.AccessoryId == accessoryId);
            if (row is null)
            {
                _db.ItemAccessoryDefaults.Add(new ItemAccessoryDefault
                {
                    ItemId = itemId,
                    AccessoryId = accessoryId,
                    QtyPerUnit = qty,
                    IsActive = true
                });
            }
            else
            {
                row.QtyPerUnit = qty;
                row.IsActive = true;
            }
        }

        foreach (var row in existing.Where(x => !seen.Contains(x.AccessoryId)))
            row.IsActive = false;

        _db.SaveChanges();
    }

    private static string GenerateNextCode(string prefix, IReadOnlyCollection<string> existingCodes)
    {
        var next = existingCodes
            .Where(code => !string.IsNullOrWhiteSpace(code) && code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(code =>
            {
                var suffix = code[prefix.Length..];
                return int.TryParse(suffix, out var n) ? n : 0;
            })
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"{prefix}{next:D4}";
    }

    private static string Require(string? value, string field)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Length == 0) throw new DomainException($"{field} is required.");
        return value;
    }

    private void Upsert<T>(Microsoft.EntityFrameworkCore.DbSet<T> set, T entity) where T : class
    {
        var id = (int)(typeof(T).GetProperty("Id")!.GetValue(entity) ?? 0);
        if (id == 0) set.Add(entity);
        else set.Update(entity);
    }
}
