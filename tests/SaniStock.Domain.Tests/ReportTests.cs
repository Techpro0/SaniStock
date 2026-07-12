using SaniStock.Domain.Models;
using Xunit;

namespace SaniStock.Domain.Tests;

public class ReportTests
{
    [Fact]
    public void Shortfall_lists_keys_where_reserved_exceeds_onhand_with_drivers()
    {
        using var h = new TestHarness();
        // 20 on hand, 50 reserved across two parties => shortfall of 30.
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 20, null));
        h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            new[] { new OrderLineInput(h.ItemA, h.Grade1, h.White, 30) },
            Array.Empty<OrderAccessoryLineInput>()));
        h.Orders.Book(new OrderInput(h.PartyY, DateTime.Today, null,
            new[] { new OrderLineInput(h.ItemA, h.Grade1, h.White, 20) },
            Array.Empty<OrderAccessoryLineInput>()));

        var shortfall = h.Reports.GetShortfall();

        var row = Assert.Single(shortfall);
        Assert.Equal(20, row.OnHand);
        Assert.Equal(50, row.Reserved);
        Assert.Equal(30, row.Shortfall);
        Assert.Equal(2, row.Drivers.Count);
        Assert.Equal(50, row.Drivers.Sum(d => d.PendingQuantity));
        Assert.Contains(row.Drivers, d => d.Party == "Acme Traders");
        Assert.Contains(row.Drivers, d => d.Party == "Best Ceramics");
    }

    [Fact]
    public void No_shortfall_when_onhand_covers_reservation()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            new[] { new OrderLineInput(h.ItemA, h.Grade1, h.White, 40) },
            Array.Empty<OrderAccessoryLineInput>()));

        Assert.Empty(h.Reports.GetShortfall());
    }

    [Fact]
    public void Dashboard_counts_today_production_dispatch_and_open_orders()
    {
        using var h = new TestHarness();
        h.Production.Post(new ProductionInput(DateTime.Today, h.ItemA, h.Grade1, h.White, 100, null));
        var order = h.Orders.Book(new OrderInput(h.PartyX, DateTime.Today, null,
            new[] { new OrderLineInput(h.ItemA, h.Grade1, h.White, 30) },
            Array.Empty<OrderAccessoryLineInput>()));
        h.Dispatch.Dispatch(new DispatchInput(order.Id, DateTime.Today, null,
            new[] { new DispatchLineInput(order.Lines.Single().Id, 10) },
            Array.Empty<DispatchAccessoryLineInput>()));

        var d = h.Reports.GetDashboard();
        Assert.Equal(100, d.TodayProductionQty);
        Assert.Equal(1, d.TodayDispatchCount);
        Assert.Equal(1, d.OpenOrderCount); // partially dispatched is still open
    }

    [Fact]
    public void Order_numbers_increment_within_year()
    {
        using var h = new TestHarness();
        var o1 = h.Orders.Book(new OrderInput(h.PartyX, new DateTime(2026, 1, 5), null,
            new[] { new OrderLineInput(h.ItemA, h.Grade1, h.White, 5) },
            Array.Empty<OrderAccessoryLineInput>()));
        var o2 = h.Orders.Book(new OrderInput(h.PartyX, new DateTime(2026, 2, 5), null,
            new[] { new OrderLineInput(h.ItemA, h.Grade1, h.White, 5) },
            Array.Empty<OrderAccessoryLineInput>()));

        Assert.Equal("ORD-2026-0001", o1.OrderNo);
        Assert.Equal("ORD-2026-0002", o2.OrderNo);
    }
}
