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
        item.Code = Require(item.Code, "Item code");
        item.Name = Require(item.Name, "Item name");
        if (_db.Items.Any(x => x.Code == item.Code && x.Id != item.Id))
            throw new DomainException($"Item code '{item.Code}' already exists.");
        Upsert(_db.Items, item);
        _db.SaveChanges();
        return item;
    }

    // ---- Accessory ----
    public Accessory SaveAccessory(Accessory a)
    {
        a.Code = Require(a.Code, "Accessory code");
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

    // ---- Grade ----
    public Grade SaveGrade(Grade g)
    {
        g.Name = Require(g.Name, "Grade name");
        if (_db.Grades.Any(x => x.Name == g.Name && x.Id != g.Id))
            throw new DomainException($"Grade '{g.Name}' already exists.");
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
