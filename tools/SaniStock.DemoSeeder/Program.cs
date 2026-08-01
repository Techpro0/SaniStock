using Microsoft.EntityFrameworkCore;
using SaniStock.Data;
using SaniStock.Data.Entities;
using SaniStock.Domain;
using SaniStock.Domain.Models;
using SaniStock.Domain.Services;

// ---------------------------------------------------------------------------
// SaniStock demo-data loader.
//
// Populates a realistic sanitary-ware dataset for client demos: product types,
// colours, items (with product types), accessories, item→accessory bundling
// recipes, customers, raw materials, finished + accessory stock, and a spread
// of orders covering every status (Booked, Partially Dispatched, Dispatched,
// Cancelled) plus a shortfall case.
//
// Everything is created THROUGH the domain services, so stock balances, ledger
// movements, reservations and order statuses are all genuinely correct.
//
// Safe to re-run:
//   * master data is get-or-create (reuses any existing colour/item/etc.),
//   * the stock + orders are created only once (guarded by a demo marker).
//
// Usage:  dotnet run --project tools/SaniStock.DemoSeeder -- "<db path>"
//         (defaults to %LOCALAPPDATA%\SaniStock\sanistock.db — the app's DB)
// ---------------------------------------------------------------------------

const string DemoTag = "DEMO";   // marks orders created by this seeder

var dbPath = args.Length > 0
    ? args[0]
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SaniStock", "sanistock.db");

Console.WriteLine($"SaniStock demo seeder → {dbPath}");

var options = new DbContextOptionsBuilder<SaniStockDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;

using var db = new SaniStockDbContext(options);
db.Database.Migrate();          // ensure schema is current
DbSeeder.EnsureSeeded(db);      // ensure grades / product types / admin exist

var user = new UserContext { Username = "demo", Role = UserRole.Admin, IsAuthenticated = true };
var stock = new StockService(db, user);
var numbers = new NumberSequenceService(db);
var master = new MasterDataService(db);
var allocations = new StockAllocationService(db, stock);
var production = new ProductionService(db, stock, user);
var packing = new PackingService(db, stock, user);
var accReceipts = new AccessoryReceiptService(db, stock, user);
var green = new GreenPieceService(db, stock, user);
var raw = new RawMaterialService(db, stock, user);
var orders = new OrderService(db, stock, allocations, numbers, user);
var dispatch = new DispatchService(db, stock, allocations, numbers, user);

int PtId(string code) => db.ProductTypes.Where(p => p.Code == code).Select(p => p.Id).First();
int GradeId(string name) => db.Grades.Where(g => g.Name == name).Select(g => g.Id).First();

var g1 = GradeId("1st");
var g2 = GradeId("2nd");

// ---- Master data (get-or-create so a re-run reuses existing rows) ----------

var colours = new Dictionary<string, int>();
int Colour(string name, string hex)
{
    var e = db.Colours.FirstOrDefault(c => c.Name == name);
    return colours[name] = e?.Id ?? master.SaveColour(new Colour { Name = name, HexCode = hex, IsActive = true }).Id;
}
foreach (var (n, h) in new[] { ("White", "#FFFFFF"), ("Ivory", "#FFFFF0"), ("Pergamon", "#EAE3D2"), ("Black", "#1C1C1C"), ("Sky Blue", "#87CEEB") })
    Colour(n, h);

// The same ware is packed and sold under several brands. Three is enough to show the Stock
// screen's per-brand columns and to let an order want a brand the factory has not packed yet.
var brands = new Dictionary<string, int>();
int BrandRow(string code, string name)
{
    var e = db.Brands.FirstOrDefault(b => b.Code == code);
    return brands[name] = e?.Id ?? master.SaveBrand(new Brand { Code = code, Name = name, IsActive = true }).Id;
}
foreach (var (c, n) in new[] { ("BR-AQUA", "Aquaria"), ("BR-CERA", "Ceramica"), ("BR-VITRA", "Vitrona") })
    BrandRow(c, n);

var acc = new Dictionary<string, int>();
int Acc(string code, string name)
{
    var e = db.Accessories.FirstOrDefault(a => a.Code == code);
    return acc[name] = e?.Id ?? master.SaveAccessory(new Accessory { Code = code, Name = name, UnitOfMeasure = "PCS", IsActive = true }).Id;
}
foreach (var (c, n) in new[]
{
    ("ACC-SEAT", "Seat Cover"), ("ACC-FLUSH", "Flush Tank Fitting"), ("ACC-PIPE", "Connection Pipe"),
    ("ACC-BOLT", "Fixing Bolt Set"), ("ACC-PILLAR", "Pillar Cock"), ("ACC-WASTE", "Waste Coupling"), ("ACC-BOTTLE", "Bottle Trap"),
})
    Acc(c, n);

var item = new Dictionary<string, int>();
int Item(string code, string name, string ptCode)
{
    var e = db.Items.FirstOrDefault(i => i.Code == code);
    return item[name] = e?.Id ?? master.SaveItem(new Item { Code = code, Name = name, UnitOfMeasure = "PCS", IsActive = true, ProductTypeId = PtId(ptCode) }).Id;
}
Item("WC-ONE", "One Piece Closet", "PT-WC");
Item("WC-TWO", "Two Piece Closet", "PT-WC");
Item("WC-EWC", "Wall Hung EWC", "PT-WC");
Item("WB-COUNT", "Counter Wash Basin", "PT-WB");
Item("WB-PED", "Pedestal Wash Basin", "PT-WB");
Item("WB-TT", "Table Top Basin", "PT-WB");
Item("URN-WM", "Wall Mounted Urinal", "PT-URN");
Item("CIST-DUAL", "Dual Flush Cistern", "PT-CIST");

// Bundling recipes (SetItemAccessoryDefaults replaces the item's recipe → idempotent).
master.SetItemAccessoryDefaults(item["One Piece Closet"], new[] { (acc["Seat Cover"], 1m), (acc["Flush Tank Fitting"], 1m), (acc["Fixing Bolt Set"], 1m) });
master.SetItemAccessoryDefaults(item["Two Piece Closet"], new[] { (acc["Seat Cover"], 1m), (acc["Flush Tank Fitting"], 1m), (acc["Connection Pipe"], 1m), (acc["Fixing Bolt Set"], 1m) });
master.SetItemAccessoryDefaults(item["Wall Hung EWC"], new[] { (acc["Seat Cover"], 1m), (acc["Fixing Bolt Set"], 1m) });
master.SetItemAccessoryDefaults(item["Counter Wash Basin"], new[] { (acc["Pillar Cock"], 1m), (acc["Waste Coupling"], 1m), (acc["Bottle Trap"], 1m) });
master.SetItemAccessoryDefaults(item["Pedestal Wash Basin"], new[] { (acc["Pillar Cock"], 1m), (acc["Waste Coupling"], 1m) });
master.SetItemAccessoryDefaults(item["Table Top Basin"], new[] { (acc["Pillar Cock"], 1m), (acc["Waste Coupling"], 1m), (acc["Bottle Trap"], 1m) });

var party = new Dictionary<string, int>();
int Party(string name, string city, string contact, string gstin)
{
    var e = db.Parties.FirstOrDefault(p => p.Name == name);
    return party[name] = e?.Id ?? master.SaveParty(new Party { Name = name, Address = city, Contact = contact, Gstin = gstin, IsActive = true }).Id;
}
Party("Shreeji Sanitary Wares", "Rajkot, Gujarat", "98250 11111", "24ABCDE1234F1Z5");
Party("Deep Ceramics", "Morbi, Gujarat", "98240 22222", "24BCDEF2345G1Z4");
Party("Royal Bath Studio", "Ahmedabad, Gujarat", "97250 33333", "24CDEFG3456H1Z3");
Party("Metro Tiles & Sanitary", "Surat, Gujarat", "99250 44444", "24DEFGH4567I1Z2");
Party("Kumar Hardware", "Mumbai, Maharashtra", "98200 55555", "27EFGHI5678J1Z1");

var rawMat = new Dictionary<string, int>();
int Raw(string name, string uom)
{
    var e = db.RawMaterials.FirstOrDefault(r => r.Name == name);
    return rawMat[name] = e?.Id ?? master.SaveRawMaterial(new RawMaterial { Name = name, UnitOfMeasure = uom, IsActive = true }).Id;
}
foreach (var (n, u) in new[] { ("Ball Clay", "KG"), ("Feldspar", "KG"), ("Quartz", "KG"), ("Glaze Frit", "KG") })
    Raw(n, u);

// ---- Stock + orders: created only once ------------------------------------
if (db.Orders.Any(o => o.Remarks != null && o.Remarks.StartsWith(DemoTag)))
{
    Console.WriteLine("Demo master data ensured; demo orders already present — skipping stock/orders.");
    PrintSummary(db);
    return;
}

var today = DateTime.Today;

void Produce(string itemName, int gradeId, string colour, decimal qty, int daysAgo) =>
    production.Post(new ProductionInput(today.AddDays(-daysAgo), item[itemName], gradeId, colours[colour], qty, "Demo production"));

Produce("One Piece Closet", g1, "White", 200, 25);
Produce("One Piece Closet", g1, "Ivory", 120, 24);
Produce("One Piece Closet", g2, "White", 40, 24);
Produce("Two Piece Closet", g1, "White", 180, 23);
Produce("Two Piece Closet", g1, "Ivory", 150, 23);
Produce("Wall Hung EWC", g1, "White", 130, 22);
Produce("Wall Hung EWC", g1, "Pergamon", 10, 22);   // deliberately low → shortfall demo
Produce("Counter Wash Basin", g1, "White", 160, 21);
Produce("Counter Wash Basin", g1, "Black", 60, 21);
Produce("Pedestal Wash Basin", g1, "White", 140, 20);
Produce("Table Top Basin", g1, "Black", 90, 19);
Produce("Table Top Basin", g1, "Sky Blue", 45, 19);
Produce("Wall Mounted Urinal", g1, "White", 100, 18);
Produce("Dual Flush Cistern", g1, "White", 220, 18);

// Pack part of what was produced, so the Stock screen shows a realistic mix of packed and
// not-yet-packed ware rather than everything sitting in one bucket. Packing is also where brand
// enters: one action can be split across brands, which is what fills the per-brand columns.
void Pack(string itemName, int gradeId, string colour, int daysAgo, params (string Brand, decimal Qty)[] split) =>
    packing.Post(new PackingInput(today.AddDays(-daysAgo), item[itemName], gradeId, colours[colour],
        split.Select(s => new PackingBrandLine(brands[s.Brand], s.Qty)).ToList(), "Demo packing"));

// Split across all three brands in one go — the worked example from the brand feature.
Pack("One Piece Closet", g1, "White", 17, ("Aquaria", 70), ("Ceramica", 50), ("Vitrona", 30));
Pack("One Piece Closet", g1, "Ivory", 17, ("Aquaria", 40), ("Ceramica", 20));
Pack("One Piece Closet", g2, "White", 17, ("Aquaria", 40));      // the 2nd-grade stock is all packed
Pack("Two Piece Closet", g1, "White", 16, ("Aquaria", 80), ("Vitrona", 40));
Pack("Two Piece Closet", g1, "Ivory", 16, ("Ceramica", 90));     // this colour is Ceramica-only
Pack("Wall Hung EWC", g1, "White", 16, ("Aquaria", 60), ("Ceramica", 40));
Pack("Counter Wash Basin", g1, "White", 15, ("Aquaria", 110));
Pack("Pedestal Wash Basin", g1, "White", 15, ("Vitrona", 140));  // fully packed, one brand
Pack("Table Top Basin", g1, "Black", 14, ("Ceramica", 40));
Pack("Wall Mounted Urinal", g1, "White", 14, ("Aquaria", 70));
Pack("Dual Flush Cistern", g1, "White", 14, ("Aquaria", 60), ("Ceramica", 60));

foreach (var a in acc.Values)
    accReceipts.Post(new AccessoryReceiptInput(today.AddDays(-26), a, 500, "Demo opening stock"));

raw.Post(new RawMaterialInput(today.AddDays(-27), rawMat["Ball Clay"], IsIssue: false, 5000, "Demo purchase"));
raw.Post(new RawMaterialInput(today.AddDays(-27), rawMat["Feldspar"], IsIssue: false, 3000, "Demo purchase"));
raw.Post(new RawMaterialInput(today.AddDays(-27), rawMat["Quartz"], IsIssue: false, 2500, "Demo purchase"));
raw.Post(new RawMaterialInput(today.AddDays(-27), rawMat["Glaze Frit"], IsIssue: false, 1200, "Demo purchase"));
raw.Post(new RawMaterialInput(today.AddDays(-10), rawMat["Ball Clay"], IsIssue: true, 800, "Issued to production"));
green.Post(new GreenPieceInput(today.AddDays(-9), item["One Piece Closet"], colours["White"], IsIssue: false, 60, "Cast today"));
green.Post(new GreenPieceInput(today.AddDays(-8), item["One Piece Closet"], colours["White"], IsIssue: true, 25, "Sent to kiln"));

OrderLineInput Line(string itemName, int gradeId, string colour, string brand, decimal qty, params int[] excludeAcc)
    => new(item[itemName], gradeId, colours[colour], brands[brand], qty,
        excludeAcc.Length > 0 ? excludeAcc : null);

// 1) Booked, awaiting dispatch — bundled accessories auto-reserved.
orders.Book(new OrderInput(party["Shreeji Sanitary Wares"], today.AddDays(-6), $"{DemoTag} standard site order",
    new[] { Line("One Piece Closet", g1, "White", "Aquaria", 50), Line("Counter Wash Basin", g1, "White", "Aquaria", 30) },
    Array.Empty<OrderAccessoryLineInput>()));

// 2) Partially dispatched (40 ordered, ship 20 now).
var order2 = orders.Book(new OrderInput(party["Deep Ceramics"], today.AddDays(-5), $"{DemoTag} split delivery",
    new[] { Line("Two Piece Closet", g1, "Ivory", "Ceramica", 40) }, Array.Empty<OrderAccessoryLineInput>()));
dispatch.Dispatch(new DispatchInput(order2.Id, today.AddDays(-3), "First lorry",
    new[] { new DispatchLineInput(order2.Lines.Single().Id, 20) },
    order2.AccessoryLines.Select(a => new DispatchAccessoryLineInput(a.Id, 20)).ToList()));

// 3) Fully dispatched.
var order3 = orders.Book(new OrderInput(party["Royal Bath Studio"], today.AddDays(-5), $"{DemoTag} showroom stock",
    new[] { Line("Pedestal Wash Basin", g1, "White", "Vitrona", 25), Line("Wall Hung EWC", g1, "White", "Aquaria", 15) },
    Array.Empty<OrderAccessoryLineInput>()));
dispatch.Dispatch(new DispatchInput(order3.Id, today.AddDays(-2), "Delivered in full",
    order3.Lines.Select(l => new DispatchLineInput(l.Id, l.QuantityOrdered)).ToList(),
    order3.AccessoryLines.Select(a => new DispatchAccessoryLineInput(a.Id, a.QuantityOrdered)).ToList()));

// 4) Booked with a per-line accessory EXCLUDED (Bottle Trap left off this order).
orders.Book(new OrderInput(party["Metro Tiles & Sanitary"], today.AddDays(-2), $"{DemoTag} customer supplies own traps",
    new[] { Line("Table Top Basin", g1, "Black", "Ceramica", 20, acc["Bottle Trap"]) }, Array.Empty<OrderAccessoryLineInput>()));

// 5) Cancelled order (reservation released for item + accessories).
var order5 = orders.Book(new OrderInput(party["Kumar Hardware"], today.AddDays(-4), $"{DemoTag} tentative",
    new[] { Line("Wall Mounted Urinal", g1, "White", "Aquaria", 30) },
    new[] { new OrderAccessoryLineInput(acc["Connection Pipe"], 30) }));
orders.Cancel(order5.Id, "Customer postponed project");

// 6) Shortfall demo — books far more than produced, driving Available negative.
orders.Book(new OrderInput(party["Shreeji Sanitary Wares"], today.AddDays(-1), $"{DemoTag} large upcoming project",
    new[] { Line("Wall Hung EWC", g1, "Pergamon", "Aquaria", 100) }, Array.Empty<OrderAccessoryLineInput>()));

// 7) Grade-fallback demo — more 1st-grade White closets than are free, so the booking exhausts
//    Aquaria's packed 1st grade, then the shared unpacked pool, and is topped up from the 40
//    packed 2nd-grade pieces (also Aquaria).
orders.Book(new OrderInput(party["Deep Ceramics"], today, $"{DemoTag} 1st grade topped up from 2nd",
    new[] { Line("One Piece Closet", g1, "White", "Aquaria", 180) }, Array.Empty<OrderAccessoryLineInput>()));

// 8) Shared-pool demo — Vitrona has nothing packed in Ivory (all 90 packed pieces are Ceramica's,
//    and it can never touch those), so this draws entirely from the shared unpacked pool. Ware in
//    the pool has no brand yet, so any order may claim it and pack it its own way later.
orders.Book(new OrderInput(party["Kumar Hardware"], today, $"{DemoTag} covered from the unpacked pool",
    new[] { Line("Two Piece Closet", g1, "Ivory", "Vitrona", 25) }, Array.Empty<OrderAccessoryLineInput>()));

PrintSummary(db);

static void PrintSummary(SaniStockDbContext db)
{
    Console.WriteLine("Demo data in database:");
    Console.WriteLine($"  Product types : {db.ProductTypes.Count()}");
    Console.WriteLine($"  Colours       : {db.Colours.Count()}");
    Console.WriteLine($"  Brands        : {db.Brands.Count()}");
    Console.WriteLine($"  Items         : {db.Items.Count()}");
    Console.WriteLine($"  Accessories   : {db.Accessories.Count()}");
    Console.WriteLine($"  Bundle recipes: {db.ItemAccessoryDefaults.Count()}");
    Console.WriteLine($"  Customers     : {db.Parties.Count()}");
    Console.WriteLine($"  Raw materials : {db.RawMaterials.Count()}");
    Console.WriteLine($"  Orders        : {db.Orders.Count()} ({string.Join(", ", db.Orders.GroupBy(o => o.Status).Select(gr => $"{gr.Key}:{gr.Count()}"))})");
    Console.WriteLine($"  Dispatches    : {db.DispatchEntries.Count()}");
    Console.WriteLine("Done.");
}
