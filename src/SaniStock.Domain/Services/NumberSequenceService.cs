using SaniStock.Data;

namespace SaniStock.Domain.Services;

/// <summary>Generates human-friendly, year-scoped document numbers (ORD-2026-0001, DN-2026-0001).</summary>
public class NumberSequenceService
{
    private readonly SaniStockDbContext _db;

    public NumberSequenceService(SaniStockDbContext db) => _db = db;

    public string NextOrderNo(int year)
    {
        var prefix = $"ORD-{year}-";
        var next = NextSuffix(_db.Orders.Where(o => o.OrderNo.StartsWith(prefix)).Select(o => o.OrderNo), prefix);
        return $"{prefix}{next:D4}";
    }

    public string NextDispatchNo(int year)
    {
        var prefix = $"DN-{year}-";
        var next = NextSuffix(_db.DispatchEntries.Where(d => d.DispatchNo.StartsWith(prefix)).Select(d => d.DispatchNo), prefix);
        return $"{prefix}{next:D4}";
    }

    private static int NextSuffix(IEnumerable<string> existing, string prefix)
    {
        var max = 0;
        foreach (var no in existing)
        {
            if (int.TryParse(no[prefix.Length..], out var n) && n > max)
                max = n;
        }
        return max + 1;
    }
}
