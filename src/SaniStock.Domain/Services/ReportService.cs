using Microsoft.EntityFrameworkCore;
using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain.Models;

namespace SaniStock.Domain.Services;

/// <summary>Read-only queries backing the stock views, reports and dashboard.</summary>
public class ReportService
{
    private readonly SaniStockDbContext _db;

    /// <summary>On-hand at or below this level (but positive) is flagged as low stock.</summary>
    public decimal LowStockThreshold { get; set; } = 10m;

    public ReportService(SaniStockDbContext db) => _db = db;

    // ---- Stock views ---------------------------------------------------------

    /// <summary>
    /// The finished-goods stock view: one row per item+grade+colour with the packed quantity broken
    /// out per brand, plus the brand columns those breakdowns are aligned to.
    /// <para>
    /// Brand splits stock across several <see cref="StockBalance"/> rows — one brand-less row for
    /// the shared unpacked pool and one per brand for packed stock — but the user thinks in terms of
    /// the physical combination, so those rows are folded back together here. Totals are therefore
    /// summed across the group, which also means <c>Available</c> is only ever judged at this level:
    /// a reservation booked against the unpacked pool can be shipped out of the brand's packed row
    /// and vice versa, so an individual row's Available is not a number to show anyone.
    /// </para>
    /// </summary>
    public FinishedStockView GetFinishedStock(bool includeZero = true)
    {
        var brands = GetStockBrandColumns();

        var balances =
            (from b in _db.StockBalances.AsNoTracking()
             join i in _db.Items on b.ItemId equals i.Id
             join g in _db.Grades on b.GradeId equals g.Id
             join c in _db.Colours on b.ColourId equals c.Id
             select new
             {
                 ItemId = i.Id, i.Code, ItemName = i.Name,
                 GradeId = g.Id, Grade = g.Name,
                 ColourId = c.Id, Colour = c.Name,
                 b.BrandId, b.RawOnHand, b.PackedOnHand, b.Reserved
             }).ToList();

        var rows = balances
            .GroupBy(b => new { b.ItemId, b.GradeId, b.ColourId })
            .Select(grp =>
            {
                var first = grp.First();
                // Index-aligned with `brands` so every consumer can lay the columns out by position.
                var packedByBrand = brands
                    .Select(col => new BrandPacked(col.BrandId, col.Name,
                        grp.Where(b => b.BrandId == col.BrandId).Sum(b => b.PackedOnHand)))
                    .ToList();

                return new StockRow(
                    first.ItemId, first.Code, first.ItemName,
                    first.GradeId, first.Grade,
                    first.ColourId, first.Colour,
                    grp.Sum(b => b.RawOnHand), grp.Sum(b => b.PackedOnHand), grp.Sum(b => b.Reserved),
                    packedByBrand);
            })
            .ToList();

        if (!includeZero)
            rows = rows.Where(r => r.OnHand != 0 || r.Reserved != 0).ToList();

        return new FinishedStockView(brands, rows
            .OrderBy(r => r.ItemName).ThenBy(r => r.Grade).ThenBy(r => r.Colour)
            .ToList());
    }

    /// <summary>
    /// The brands worth a column: every active brand, plus any deactivated brand still holding
    /// packed stock — finished ware or accessories. Without that second group, deactivating a brand
    /// would quietly hide real, shippable goods — the same reason the item setup screen keeps
    /// listing an inactive accessory that is still in a recipe.
    /// </summary>
    public List<BrandColumn> GetStockBrandColumns()
    {
        var brandsWithStock = _db.StockBalances.AsNoTracking()
            .Where(b => b.BrandId != null && b.PackedOnHand != 0)
            .Select(b => b.BrandId!.Value)
            .Distinct()
            .ToList();
        var brandsWithAccessoryStock = _db.AccessoryStockBalances.AsNoTracking()
            .Where(b => b.BrandId != null && b.PackedOnHand != 0)
            .Select(b => b.BrandId!.Value)
            .Distinct()
            .ToList();
        var brandsHoldingStock = brandsWithStock.Concat(brandsWithAccessoryStock).ToHashSet();

        return _db.Brands.AsNoTracking()
            .Where(b => b.IsActive || brandsHoldingStock.Contains(b.Id))
            .OrderBy(b => b.Name)
            .Select(b => new BrandColumn(b.Id, b.Code, b.Name))
            .ToList();
    }

    /// <summary>
    /// The accessory stock view: one row per accessory with the packed quantity broken out per
    /// brand, plus the brand columns those breakdowns are aligned to. Mirrors <see cref="GetFinishedStock"/>.
    /// </summary>
    public AccessoryStockView GetAccessoryStock(bool includeZero = true)
    {
        var brands = GetStockBrandColumns();

        var balances =
            (from b in _db.AccessoryStockBalances.AsNoTracking()
             join a in _db.Accessories on b.AccessoryId equals a.Id
             select new { AccessoryId = a.Id, a.Code, Name = a.Name, b.BrandId, b.RawOnHand, b.PackedOnHand, b.Reserved })
            .ToList();

        var rows = balances
            .GroupBy(b => b.AccessoryId)
            .Select(grp =>
            {
                var first = grp.First();
                var packedByBrand = brands
                    .Select(col => new BrandPacked(col.BrandId, col.Name,
                        grp.Where(b => b.BrandId == col.BrandId).Sum(b => b.PackedOnHand)))
                    .ToList();

                return new AccessoryStockRow(
                    first.AccessoryId, first.Code, first.Name,
                    grp.Sum(b => b.RawOnHand), grp.Sum(b => b.PackedOnHand), grp.Sum(b => b.Reserved),
                    packedByBrand);
            })
            .ToList();

        if (!includeZero)
            rows = rows.Where(r => r.OnHand != 0 || r.Reserved != 0).ToList();

        return new AccessoryStockView(brands, rows.OrderBy(r => r.AccessoryName).ToList());
    }

    // ---- Shortfall / production planning -------------------------------------

    /// <summary>
    /// Every finished combination where Reserved &gt; OnHand, with the open orders driving it.
    /// This is the "what to produce next" report.
    /// <para>
    /// Drivers are attributed to the grade whose stock the reservation was actually drawn from, not
    /// to the grade printed on the order line. Without that, a 2nd-grade shortfall created by a
    /// 1st-grade order borrowing 2nd-grade stock would show up with no orders explaining it.
    /// Because each grade's Reserved only ever counts what was drawn from that grade, Available per
    /// grade already nets out cross-grade reservations — no stock is counted twice.
    /// </para>
    /// <para>
    /// <b>Brand is context here, not a grouping.</b> A shortfall of packed Brand-A stock is still
    /// cured by producing more of the item+grade+colour, because production lands in the brand-less
    /// unpacked pool and only becomes branded at packing — there is nothing you can "produce for
    /// Brand A". So the report stays keyed by item+grade+colour, measured on the combination's
    /// totals, and names each driving order's brand alongside the grade it was ordered as.
    /// </para>
    /// <para>
    /// One consequence is deliberate: because the measure is the combination's total, stock that is
    /// packed under the <em>wrong</em> brand does not register. 100 packed for Brand B against 100
    /// ordered for Brand A nets to zero and never appears, even though that order cannot ship. The
    /// remedy there is repacking, which this app does not model, and recommending production would
    /// simply over-produce.
    /// </para>
    /// </summary>
    public List<ShortfallRow> GetShortfall()
    {
        var shortKeys = GetFinishedStock().Rows.Where(r => r.Reserved > r.OnHand).ToList();
        if (shortKeys.Count == 0) return new List<ShortfallRow>();

        // Open order lines that could drive a shortfall.
        var openLines =
            (from o in _db.Orders.AsNoTracking()
             where o.Status == OrderStatus.Booked || o.Status == OrderStatus.PartiallyDispatched
             join l in _db.OrderLines.AsNoTracking() on o.Id equals l.OrderId
             join p in _db.Parties.AsNoTracking() on o.PartyId equals p.Id
             join lg in _db.Grades.AsNoTracking() on l.GradeId equals lg.Id
             join br in _db.Brands.AsNoTracking() on l.BrandId equals br.Id
             where l.QuantityOrdered > l.QuantityDispatched
             select new
             {
                 LineId = l.Id,
                 l.ItemId, l.GradeId, l.ColourId,
                 o.OrderNo, Party = p.Name, o.OrderDate, OrderedGrade = lg.Name, Brand = br.Name,
                 Pending = l.QuantityOrdered - l.QuantityDispatched
             }).ToList();

        var openLineIds = openLines.Select(x => x.LineId).ToHashSet();
        var allocationsByLine = _db.OrderLineAllocations.AsNoTracking()
            .Where(a => openLineIds.Contains(a.OrderLineId))
            .Select(a => new { a.OrderLineId, a.GradeId, a.Quantity, a.QuantityDispatched, a.QuantityReleased })
            .ToList()
            .GroupBy(a => a.OrderLineId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Fan each open line out to the grade(s) it is actually holding stock against.
        var driversByKey = new Dictionary<(int ItemId, int GradeId, int ColourId), List<ShortfallDriver>>();
        foreach (var l in openLines)
        {
            if (allocationsByLine.TryGetValue(l.LineId, out var allocations))
            {
                // A line can hold packed and unpacked stock of the same grade — under its brand and
                // in the shared pool — and that is one order waiting on one combination, so it
                // stays a single driver row. Grouping by grade alone keeps it that way.
                foreach (var byGrade in allocations.GroupBy(a => a.GradeId))
                {
                    var stillHeld = byGrade.Sum(a => a.Quantity - a.QuantityDispatched - a.QuantityReleased);
                    if (stillHeld <= 0) continue;
                    Add((l.ItemId, byGrade.Key, l.ColourId),
                        new ShortfallDriver(l.OrderNo, l.Party, l.OrderDate, stillHeld, l.OrderedGrade, l.Brand));
                }
            }
            else
            {
                // Booked before allocations were recorded: it holds against its own grade.
                Add((l.ItemId, l.GradeId, l.ColourId),
                    new ShortfallDriver(l.OrderNo, l.Party, l.OrderDate, l.Pending, l.OrderedGrade, l.Brand));
            }
        }

        var result = new List<ShortfallRow>();
        foreach (var k in shortKeys)
        {
            var drivers = driversByKey.TryGetValue((k.ItemId, k.GradeId, k.ColourId), out var found)
                ? found.OrderBy(d => d.OrderDate).ToList()
                : new List<ShortfallDriver>();

            result.Add(new ShortfallRow(
                k.ItemId, k.ItemCode, k.ItemName, k.Grade, k.Colour,
                k.RawOnHand, k.PackedOnHand, k.Reserved, k.Reserved - k.OnHand, drivers));
        }
        return result
            .OrderByDescending(r => r.Shortfall)
            .ThenBy(r => r.ItemName)
            .ToList();

        void Add((int, int, int) key, ShortfallDriver driver)
        {
            if (!driversByKey.TryGetValue(key, out var list))
                driversByKey[key] = list = new List<ShortfallDriver>();
            list.Add(driver);
        }
    }

    // ---- Production ----------------------------------------------------------

    public List<ProductionReportRow> GetProduction(DateTime fromDate, DateTime toDate, int? itemId = null)
    {
        var q =
            from e in _db.ProductionEntries.AsNoTracking()
            join i in _db.Items on e.ItemId equals i.Id
            join g in _db.Grades on e.GradeId equals g.Id
            join c in _db.Colours on e.ColourId equals c.Id
            where e.Date >= fromDate.Date && e.Date < toDate.Date.AddDays(1)
            where itemId == null || e.ItemId == itemId
            orderby e.Date descending, i.Name
            select new ProductionReportRow(
                e.Date, i.Code, i.Name, g.Name, c.Name, e.Quantity, e.IsReversal, e.CreatedBy);
        return q.ToList();
    }

    // ---- Orders --------------------------------------------------------------

    public List<OrderReportRow> GetOrders(DateTime fromDate, DateTime toDate, int? partyId = null)
    {
        var itemRows =
            from o in _db.Orders.AsNoTracking()
            where o.OrderDate >= fromDate.Date && o.OrderDate < toDate.Date.AddDays(1)
            where partyId == null || o.PartyId == partyId
            join p in _db.Parties on o.PartyId equals p.Id
            // Editing an order zeroes the lines it superseded rather than removing them, so the
            // history survives. They are not part of the order any more, so reports skip them.
            join l in _db.OrderLines.Where(x => x.QuantityOrdered > 0) on o.Id equals l.OrderId
            join i in _db.Items on l.ItemId equals i.Id
            join g in _db.Grades on l.GradeId equals g.Id
            join c in _db.Colours on l.ColourId equals c.Id
            join br in _db.Brands on l.BrandId equals br.Id
            select new OrderReportRow(
                o.OrderNo, o.OrderDate, p.Name, o.Status.ToString(),
                i.Name, g.Name, c.Name, br.Name,
                l.QuantityOrdered, l.QuantityDispatched, l.QuantityOrdered - l.QuantityDispatched);

        var accRows =
            from o in _db.Orders.AsNoTracking()
            where o.OrderDate >= fromDate.Date && o.OrderDate < toDate.Date.AddDays(1)
            where partyId == null || o.PartyId == partyId
            join p in _db.Parties on o.PartyId equals p.Id
            join l in _db.OrderAccessoryLines.Where(x => x.QuantityOrdered > 0) on o.Id equals l.OrderId
            join a in _db.Accessories on l.AccessoryId equals a.Id
            select new OrderReportRow(
                o.OrderNo, o.OrderDate, p.Name, o.Status.ToString(),
                a.Name, "-", "-", "-",
                l.QuantityOrdered, l.QuantityDispatched, l.QuantityOrdered - l.QuantityDispatched);

        return itemRows.ToList().Concat(accRows.ToList())
            .OrderByDescending(r => r.OrderDate).ThenBy(r => r.OrderNo)
            .ToList();
    }

    // ---- Dashboard -----------------------------------------------------------

    public DashboardSummary GetDashboard()
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        var todayProduction = _db.ProductionEntries
            .Where(e => e.Date >= today && e.Date < tomorrow)
            .Sum(e => (decimal?)(e.IsReversal ? -e.Quantity : e.Quantity)) ?? 0m;

        var todayDispatches = _db.DispatchEntries.Count(d => d.Date >= today && d.Date < tomorrow);

        var finished = GetFinishedStock().Rows;
        var shortfallCount = finished.Count(r => r.Reserved > r.OnHand);
        var lowStockCount = finished.Count(r => r.OnHand > 0 && r.OnHand <= LowStockThreshold);

        var openOrders = _db.Orders.Count(o =>
            o.Status == OrderStatus.Booked || o.Status == OrderStatus.PartiallyDispatched);

        return new DashboardSummary(todayProduction, todayDispatches, shortfallCount, lowStockCount, openOrders);
    }
}
