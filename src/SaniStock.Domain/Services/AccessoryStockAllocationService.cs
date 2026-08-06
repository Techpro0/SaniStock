using SaniStock.Data;
using SaniStock.Data.Entities;

namespace SaniStock.Domain.Services;

/// <summary>
/// One source an accessory booking drew from, in the order it was drawn. <paramref name="BrandId"/>
/// is null for the shared unpacked pool and for a shortfall. Mirrors <see cref="AllocationStep"/>,
/// minus grade — accessories aren't graded.
/// </summary>
public record AccessoryAllocationStep(int? BrandId, StockBucket Bucket, decimal Quantity, int Priority);

/// <summary>
/// A planned draw against one recorded allocation. <see cref="Allocation"/> is null for the fallback
/// draw used by lines booked before allocations were recorded.
/// </summary>
public record AccessoryAllocationDraw(OrderAccessoryLineAllocation? Allocation, int? BrandId, decimal Quantity);

/// <summary>
/// Decides which physical accessory stock covers a booked order accessory line. Mirrors
/// <see cref="StockAllocationService"/> for finished ware, minus the grade chain: an accessory line
/// draws at most two sources — this line's brand's packed stock, then the shared unpacked pool —
/// because there is no grade to borrow from below.
/// <para>
/// This is what makes an auto-bundled accessory prefer its parent item line's brand: the line
/// carries that brand, so <see cref="Plan"/> tries the brand's packed accessory stock first, falling
/// back to the shared pool exactly as an item line already does.
/// </para>
/// Booking is never blocked by a lack of stock; this only decides where the reservation lands.
/// </summary>
public class AccessoryStockAllocationService
{
    private readonly SaniStockDbContext _db;
    private readonly StockService _stock;

    public AccessoryStockAllocationService(SaniStockDbContext db, StockService stock)
    {
        _db = db;
        _stock = stock;
    }

    /// <summary>
    /// Plans how <paramref name="quantity"/> of an accessory ordered under <paramref name="brandId"/>
    /// is covered: that brand's packed stock first, then the shared unpacked pool. The returned steps
    /// always sum to <paramref name="quantity"/>; the last one may be a <see cref="StockBucket.Shortfall"/>
    /// step when stock ran out.
    /// </summary>
    public List<AccessoryAllocationStep> Plan(int accessoryId, int brandId, decimal quantity)
    {
        var steps = new List<AccessoryAllocationStep>();
        var remaining = quantity;

        var packedFree = StockService.FreeOnAccessory(_stock.FindAccessoryBalance(accessoryId, brandId));
        var rawFree = StockService.FreeOnAccessory(_stock.FindAccessoryBalance(accessoryId, null));

        Take(brandId, StockBucket.Packed, packedFree);
        Take(null, StockBucket.Raw, rawFree);

        if (remaining > 0)
            steps.Add(new AccessoryAllocationStep(null, StockBucket.Shortfall, remaining, steps.Count));

        return steps;

        void Take(int? fromBrandId, StockBucket bucket, decimal available)
        {
            var take = Math.Min(remaining, available);
            if (take <= 0) return;
            steps.Add(new AccessoryAllocationStep(fromBrandId, bucket, take, steps.Count));
            remaining -= take;
        }
    }

    // ---- Consuming what was allocated ---------------------------------------

    /// <summary>Plans which recorded sources cover a shipment. Mirrors <see cref="StockAllocationService.PlanDispatch"/>.</summary>
    public List<AccessoryAllocationDraw> PlanDispatch(OrderAccessoryLine line, decimal quantity) =>
        Draw(line, quantity, ascending: true);

    /// <summary>Plans which sources give back a released reservation. Mirrors <see cref="StockAllocationService.PlanRelease"/>.</summary>
    public List<AccessoryAllocationDraw> PlanRelease(OrderAccessoryLine line, decimal quantity) =>
        Draw(line, quantity, ascending: false);

    /// <summary>Records planned draws as shipped against their allocations.</summary>
    public static void MarkDispatched(IEnumerable<AccessoryAllocationDraw> draws)
    {
        foreach (var d in draws)
            if (d.Allocation != null) d.Allocation.QuantityDispatched += d.Quantity;
    }

    /// <summary>Records planned draws as released against their allocations.</summary>
    public static void MarkReleased(IEnumerable<AccessoryAllocationDraw> draws)
    {
        foreach (var d in draws)
            if (d.Allocation != null) d.Allocation.QuantityReleased += d.Quantity;
    }

    /// <summary>
    /// Totals draws per source row (brand, null meaning the shared unpacked pool), keeping the order
    /// the sources were first drawn in.
    /// </summary>
    public static List<(int? BrandId, decimal Quantity)> BySource(IEnumerable<AccessoryAllocationDraw> draws)
    {
        var perSource = new List<(int? BrandId, decimal Quantity)>();
        foreach (var d in draws)
        {
            var at = perSource.FindIndex(x => x.BrandId == d.BrandId);
            if (at >= 0) perSource[at] = (d.BrandId, perSource[at].Quantity + d.Quantity);
            else perSource.Add((d.BrandId, d.Quantity));
        }
        return perSource;
    }

    private List<AccessoryAllocationDraw> Draw(OrderAccessoryLine line, decimal quantity, bool ascending)
    {
        var draws = new List<AccessoryAllocationDraw>();
        var remaining = quantity;

        var allocations = _db.OrderAccessoryLineAllocations.Where(a => a.OrderAccessoryLineId == line.Id).ToList();
        allocations = ascending
            ? allocations.OrderBy(a => a.Priority).ToList()
            : allocations.OrderByDescending(a => a.Priority).ToList();

        foreach (var a in allocations)
        {
            if (remaining <= 0) break;
            var take = Math.Min(remaining, a.QuantityReserved);
            if (take <= 0) continue;
            draws.Add(new AccessoryAllocationDraw(a, a.BrandId, take));
            remaining -= take;
        }

        // Lines with no recorded allocation (should not occur once bookings always allocate, but
        // mirrors the same defensive fallback StockAllocationService.Draw has for pre-allocation
        // OrderLines) fall back to the shared unpacked pool.
        if (remaining > 0) draws.Add(new AccessoryAllocationDraw(null, null, remaining));

        return draws;
    }
}
