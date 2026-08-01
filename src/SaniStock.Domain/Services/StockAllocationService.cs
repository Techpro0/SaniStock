using SaniStock.Data;
using SaniStock.Data.Entities;

namespace SaniStock.Domain.Services;

/// <summary>
/// One source a booking drew from, in the order it was drawn. <paramref name="BrandId"/> is null
/// for the shared unpacked pool and for a shortfall.
/// </summary>
public record AllocationStep(int GradeId, int? BrandId, StockBucket Bucket, decimal Quantity, int Priority);

/// <summary>
/// A planned draw against one recorded allocation. <see cref="Allocation"/> is null for the
/// fallback draw used by lines booked before allocations were recorded.
/// </summary>
public record AllocationDraw(OrderLineAllocation? Allocation, int GradeId, int? BrandId, decimal Quantity);

/// <summary>
/// How a reservation for one item+grade+colour+brand is actually taken out of stock: some from the
/// brand's packed row, some from the shared unpacked row.
/// </summary>
public record PhysicalDraw(decimal FromPacked, decimal FromRaw)
{
    public decimal Total => FromPacked + FromRaw;
}

/// <summary>
/// Decides which physical stock covers a booked order line.
/// <para>
/// A top-grade line is allowed to fall back to the next grade down when its own stock runs out —
/// 1st-grade goods can be met from 2nd-grade stock, but never the other way round. Within each
/// grade, packed stock is drawn before unpacked, so the goods needing least handling are claimed
/// first. Any quantity the sources cannot cover is recorded as a shortfall against the ordered
/// grade, which is what drives that grade's Available negative and puts it on the
/// "what to make" report.
/// </para>
/// <para>
/// <b>Brand narrows the packed steps only.</b> "Packed 1st grade" means packed 1st grade
/// <em>for this line's brand</em>: another brand's packed pieces are in the wrong boxes and can
/// never cover this order. The unpacked steps stay brand-less, because ware only becomes branded
/// when someone packs it — any order may draw against the shared pool regardless of the brand it
/// was placed for. The chain is therefore
/// packed(brand) 1st → unpacked 1st → packed(brand) 2nd → unpacked 2nd → shortfall.
/// </para>
/// Booking is never blocked by a lack of stock; this only decides where the reservation lands.
/// </summary>
public class StockAllocationService
{
    private readonly SaniStockDbContext _db;
    private readonly StockService _stock;

    public StockAllocationService(SaniStockDbContext db, StockService stock)
    {
        _db = db;
        _stock = stock;
    }

    /// <summary>
    /// The grades a line of <paramref name="gradeId"/> may draw from, best first. Only the top
    /// grade (lowest sort order among active grades) borrows, and only from the grade immediately
    /// below it; every other grade stands alone. Grades are user-maintained master data, so the
    /// chain is derived from sort order rather than from any hard-coded name or id.
    /// </summary>
    public List<int> ResolveGradeChain(int gradeId)
    {
        var ranked = _db.Grades
            .Where(g => g.IsActive)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
            .Select(g => g.Id)
            .Take(2)
            .ToList();

        return ranked.Count == 2 && ranked[0] == gradeId
            ? new List<int> { ranked[0], ranked[1] }
            : new List<int> { gradeId };
    }

    /// <summary>
    /// Plans how <paramref name="quantity"/> of an item+colour ordered under
    /// <paramref name="brandId"/> is covered, walking the grade chain and taking that brand's
    /// packed stock before the shared unpacked pool within each grade. The returned steps always
    /// sum to <paramref name="quantity"/>; the last one may be a <see cref="StockBucket.Shortfall"/>
    /// step against <paramref name="gradeId"/> when stock ran out.
    /// </summary>
    public List<AllocationStep> Plan(int itemId, int gradeId, int colourId, int brandId, decimal quantity)
    {
        var steps = new List<AllocationStep>();
        var remaining = quantity;

        foreach (var chainGradeId in ResolveGradeChain(gradeId))
        {
            if (remaining <= 0) break;

            var packedFree = StockService.FreeOn(
                _stock.FindFinishedBalance(itemId, chainGradeId, colourId, brandId));
            var rawFree = StockService.FreeOn(
                _stock.FindFinishedBalance(itemId, chainGradeId, colourId, null));

            Take(chainGradeId, brandId, StockBucket.Packed, packedFree);
            Take(chainGradeId, null, StockBucket.Raw, rawFree);
        }

        // A shortfall has no physical source, so it carries no brand: the cure is producing more,
        // and production lands in the brand-less unpacked pool.
        if (remaining > 0)
            steps.Add(new AllocationStep(gradeId, null, StockBucket.Shortfall, remaining, steps.Count));

        return steps;

        void Take(int fromGradeId, int? fromBrandId, StockBucket bucket, decimal available)
        {
            var take = Math.Min(remaining, available);
            if (take <= 0) return;
            steps.Add(new AllocationStep(fromGradeId, fromBrandId, bucket, take, steps.Count));
            remaining -= take;
        }
    }

    // ---- Consuming what was allocated ---------------------------------------

    /// <summary>
    /// Plans which recorded sources cover a shipment of <paramref name="quantity"/> from a line,
    /// best source first. Nothing is mutated, so a dispatch can be fully validated before any stock
    /// moves. Dispatch honours each allocation's recorded <em>grade</em> — that is where the
    /// reservation sits and where the goods physically are — but not its recorded bucket, because
    /// packing may legitimately have moved the quantity from unpacked to packed since booking.
    /// The recorded <em>brand</em> says which row the reservation must be released from;
    /// <see cref="PlanPhysicalDraw"/> then decides which row the goods actually come out of.
    /// </summary>
    public List<AllocationDraw> PlanDispatch(OrderLine line, decimal quantity) =>
        Draw(line, quantity, ascending: true);

    /// <summary>
    /// Plans which sources give back a released reservation of <paramref name="quantity"/>, unwinding
    /// in reverse draw order so a borrowed lower grade is handed back before the line's own grade.
    /// Nothing is mutated.
    /// </summary>
    public List<AllocationDraw> PlanRelease(OrderLine line, decimal quantity) =>
        Draw(line, quantity, ascending: false);

    /// <summary>Records planned draws as shipped against their allocations.</summary>
    public static void MarkDispatched(IEnumerable<AllocationDraw> draws)
    {
        foreach (var d in draws)
            if (d.Allocation != null) d.Allocation.QuantityDispatched += d.Quantity;
    }

    /// <summary>Records planned draws as released against their allocations.</summary>
    public static void MarkReleased(IEnumerable<AllocationDraw> draws)
    {
        foreach (var d in draws)
            if (d.Allocation != null) d.Allocation.QuantityReleased += d.Quantity;
    }

    /// <summary>
    /// Totals draws per source row (grade + brand, brand null meaning the shared unpacked pool),
    /// keeping the order the sources were first drawn in. Each entry is one balance row whose
    /// Reserved has to move.
    /// </summary>
    public static List<(int GradeId, int? BrandId, decimal Quantity)> BySource(IEnumerable<AllocationDraw> draws)
    {
        var perSource = new List<(int GradeId, int? BrandId, decimal Quantity)>();
        foreach (var d in draws)
        {
            var at = perSource.FindIndex(x => x.GradeId == d.GradeId && x.BrandId == d.BrandId);
            if (at >= 0) perSource[at] = (d.GradeId, d.BrandId, perSource[at].Quantity + d.Quantity);
            else perSource.Add((d.GradeId, d.BrandId, d.Quantity));
        }
        return perSource;
    }

    /// <summary>
    /// Splits a shipment of <paramref name="quantity"/> across the two rows a brand's goods can
    /// physically be in: that brand's packed stock and the shared unpacked pool. Packed goes first,
    /// because it is ready to load — the same rule dispatch has always followed.
    /// <para>
    /// Brand does not change the preference, only the choice of rows: the caller passes this
    /// brand's packed quantity and the shared pool, so another brand's boxes are never in scope.
    /// Being willing to draw from either row is what keeps the ordinary flow working — goods booked
    /// while unpacked and packed under this brand before shipping are still shippable, and so are
    /// goods booked as packed whose packing was later undone.
    /// </para>
    /// <para>
    /// Taking packed first also leaves the shared pool as large as possible, which matters more now
    /// than it did: unpacked ware can still become any brand, so spending it last keeps the
    /// factory's options open.
    /// </para>
    /// Returns whatever the two rows can cover, which may be less than the quantity asked for; the
    /// caller compares <see cref="PhysicalDraw.Total"/> against the demand and rejects the dispatch
    /// if stock is short. Pure function — nothing is read from or written to the database.
    /// </summary>
    public static PhysicalDraw PlanPhysicalDraw(decimal quantity, decimal rawAvailable, decimal packedAvailable)
    {
        quantity = Math.Max(0m, quantity);
        rawAvailable = Math.Max(0m, rawAvailable);
        packedAvailable = Math.Max(0m, packedAvailable);

        var fromPacked = Math.Min(quantity, packedAvailable);
        var fromRaw = Math.Min(quantity - fromPacked, rawAvailable);

        return new PhysicalDraw(fromPacked, fromRaw);
    }

    private List<AllocationDraw> Draw(OrderLine line, decimal quantity, bool ascending)
    {
        var draws = new List<AllocationDraw>();
        var remaining = quantity;

        var allocations = _db.OrderLineAllocations.Where(a => a.OrderLineId == line.Id).ToList();
        allocations = ascending
            ? allocations.OrderBy(a => a.Priority).ToList()
            : allocations.OrderByDescending(a => a.Priority).ToList();

        foreach (var a in allocations)
        {
            if (remaining <= 0) break;
            var take = Math.Min(remaining, a.QuantityReserved);
            if (take <= 0) continue;
            draws.Add(new AllocationDraw(a, a.GradeId, a.BrandId, take));
            remaining -= take;
        }

        // Lines booked before allocations were recorded fall back to the line's own grade and the
        // shared unpacked pool — exactly how every line behaved before cross-grade allocation
        // existed, and where the Brand migration left their reservations.
        if (remaining > 0) draws.Add(new AllocationDraw(null, line.GradeId, null, remaining));

        return draws;
    }
}
