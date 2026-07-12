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

    public List<StockRow> GetFinishedStock(bool includeZero = true)
    {
        var q =
            from b in _db.StockBalances.AsNoTracking()
            join i in _db.Items on b.ItemId equals i.Id
            join g in _db.Grades on b.GradeId equals g.Id
            join c in _db.Colours on b.ColourId equals c.Id
            select new StockRow(i.Id, i.Code, i.Name, g.Id, g.Name, c.Id, c.Name, b.OnHand, b.Reserved);

        var rows = q.ToList();
        if (!includeZero)
            rows = rows.Where(r => r.OnHand != 0 || r.Reserved != 0).ToList();
        return rows
            .OrderBy(r => r.ItemName).ThenBy(r => r.Grade).ThenBy(r => r.Colour)
            .ToList();
    }

    public List<AccessoryStockRow> GetAccessoryStock(bool includeZero = true)
    {
        var q =
            from b in _db.AccessoryStockBalances.AsNoTracking()
            join a in _db.Accessories on b.AccessoryId equals a.Id
            select new AccessoryStockRow(a.Id, a.Code, a.Name, b.OnHand, b.Reserved);

        var rows = q.ToList();
        if (!includeZero)
            rows = rows.Where(r => r.OnHand != 0 || r.Reserved != 0).ToList();
        return rows.OrderBy(r => r.AccessoryName).ToList();
    }

    // ---- Shortfall / production planning -------------------------------------

    /// <summary>
    /// Every finished combination where Reserved &gt; OnHand, with the open orders driving it.
    /// This is the "what to produce next" report.
    /// </summary>
    public List<ShortfallRow> GetShortfall()
    {
        var shortKeys = GetFinishedStock().Where(r => r.Reserved > r.OnHand).ToList();
        if (shortKeys.Count == 0) return new List<ShortfallRow>();

        // Open order lines that could drive a shortfall.
        var openLines =
            (from o in _db.Orders.AsNoTracking()
             where o.Status == OrderStatus.Booked || o.Status == OrderStatus.PartiallyDispatched
             join l in _db.OrderLines.AsNoTracking() on o.Id equals l.OrderId
             join p in _db.Parties.AsNoTracking() on o.PartyId equals p.Id
             where l.QuantityOrdered > l.QuantityDispatched
             select new
             {
                 l.ItemId, l.GradeId, l.ColourId,
                 o.OrderNo, Party = p.Name, o.OrderDate,
                 Pending = l.QuantityOrdered - l.QuantityDispatched
             }).ToList();

        var result = new List<ShortfallRow>();
        foreach (var k in shortKeys)
        {
            var drivers = openLines
                .Where(x => x.ItemId == k.ItemId && x.GradeId == k.GradeId && x.ColourId == k.ColourId)
                .OrderBy(x => x.OrderDate)
                .Select(x => new ShortfallDriver(x.OrderNo, x.Party, x.OrderDate, x.Pending))
                .ToList();

            result.Add(new ShortfallRow(
                k.ItemId, k.ItemCode, k.ItemName, k.Grade, k.Colour,
                k.OnHand, k.Reserved, k.Reserved - k.OnHand, drivers));
        }
        return result
            .OrderByDescending(r => r.Shortfall)
            .ThenBy(r => r.ItemName)
            .ToList();
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
            join l in _db.OrderLines on o.Id equals l.OrderId
            join i in _db.Items on l.ItemId equals i.Id
            join g in _db.Grades on l.GradeId equals g.Id
            join c in _db.Colours on l.ColourId equals c.Id
            select new OrderReportRow(
                o.OrderNo, o.OrderDate, p.Name, o.Status.ToString(),
                i.Name, g.Name, c.Name,
                l.QuantityOrdered, l.QuantityDispatched, l.QuantityOrdered - l.QuantityDispatched);

        var accRows =
            from o in _db.Orders.AsNoTracking()
            where o.OrderDate >= fromDate.Date && o.OrderDate < toDate.Date.AddDays(1)
            where partyId == null || o.PartyId == partyId
            join p in _db.Parties on o.PartyId equals p.Id
            join l in _db.OrderAccessoryLines on o.Id equals l.OrderId
            join a in _db.Accessories on l.AccessoryId equals a.Id
            select new OrderReportRow(
                o.OrderNo, o.OrderDate, p.Name, o.Status.ToString(),
                a.Name, "-", "-",
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

        var finished = GetFinishedStock();
        var shortfallCount = finished.Count(r => r.Reserved > r.OnHand);
        var lowStockCount = finished.Count(r => r.OnHand > 0 && r.OnHand <= LowStockThreshold);

        var openOrders = _db.Orders.Count(o =>
            o.Status == OrderStatus.Booked || o.Status == OrderStatus.PartiallyDispatched);

        return new DashboardSummary(todayProduction, todayDispatches, shortfallCount, lowStockCount, openOrders);
    }
}
