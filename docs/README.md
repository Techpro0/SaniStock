# SaniStock

Offline, installable **Windows desktop app** for a sanitary-ware (ceramics) manufacturer.
It tracks the flow **Production → Packing → Stock → Order Booking (reservation) → Order Dispatch (deduction)**,
with PDF/Excel reporting and a shortfall-driven production-planning report.

- **.NET 8 · WPF (MVVM, CommunityToolkit.Mvvm)**
- **SQLite via EF Core** — a single local file, no server
- **QuestPDF** reports · **ClosedXML** Excel export · **Serilog** logging · **BCrypt** password hashing
- Fully offline — the app makes no network calls

---

## Solution layout

```
SaniStock.slnx
  src/
    SaniStock.Data      EF Core entities, DbContext, migrations, seeder
    SaniStock.Domain    Business services (the inventory math) + unit-tested
    SaniStock.Reports   QuestPDF report definitions
    SaniStock.App       WPF UI (Views + ViewModels)
  tests/
    SaniStock.Domain.Tests   xUnit tests for the reservation/dispatch math
  installer/            Inno Setup script
  docs/                 This file + schema notes
```

## Build & run (developer)

```bash
# from the repo root
dotnet build                 # builds the whole solution (SaniStock.slnx)
dotnet test                  # runs the domain unit tests
dotnet run --project src/SaniStock.App
```

On first run the app:
1. creates `%LOCALAPPDATA%\SaniStock\` (database + logs),
2. applies EF Core migrations to `sanistock.db`,
3. seeds grades (1st/2nd/3rd), the `Unbranded` brand and a default admin.

Upgrading an existing database, in migration order:

- the **packing** migration moved all current stock into the **unpacked** bucket and gave every
  still-open order line the allocation row it would have been booked with, so nothing was blocked —
  dispatch falls back to unpacked stock, and the *Packing* screen is how stock moves into the packed
  bucket from then on.
- the **brand** migration assigns all pre-existing packed stock, packing entries and order lines to a
  seeded **`Unbranded`** brand, left active so that stock stays visible and shippable. Add your real
  brands under *Setup Lists → Brands*, and deactivate `Unbranded` once the old stock has sold through.
  Existing reservations stay whole on the shared unpacked pool; nothing needs re-booking. See
  [schema.md](schema.md) and `PROJECT_OVERVIEW.md` §5 for exactly what moves where.

**Default login — username `admin`, password `admin123`.** Change it from *Users* after first sign-in.

## Where data lives

| What | Location |
|------|----------|
| Database | `%LOCALAPPDATA%\SaniStock\sanistock.db` |
| Logs | `%LOCALAPPDATA%\SaniStock\logs\log-*.txt` (14-day rolling) |
| Backups | wherever you choose in *Backup → Backup now* |

## Publish + build the installer

The installer bundles the .NET runtime (self-contained) so it runs on machines without .NET.

```bash
# 1) self-contained publish
dotnet publish src/SaniStock.App/SaniStock.App.csproj -c Release -r win-x64 --self-contained true -o publish

# 2) compile the installer (requires Inno Setup 6 — https://jrsoftware.org/isdl.php)
cd installer
iscc SaniStock.iss
# -> installer/Output/SaniStock-Setup-x64.exe
```

## Key business rules (implemented)

1. **Production** increases the **unpacked** part of finished on-hand for an item+grade+colour (creating
   the combination if new). Newly made ware is not packed until a packing entry says so.
2. **Packing** moves quantity from unpacked to packed for an item+grade+colour, and is where **brand**
   is decided. Total stock is unchanged — only the split moves — and you cannot pack more than is
   unpacked. `In Stock = Not Packed + Packed`.
3. **Brand exists from packing onward.** Unpacked ware is one shared, brand-less pool; packed ware
   belongs to exactly one brand. A single packing can be split across several brands at once (500 as
   200/200/100), and each brand's portion is undone on its own.
4. **Order booking** increases *reserved* only — never on-hand — and is **never blocked** by stock.
   A negative `Available (= OnHand − Reserved)` is the shortfall signal, surfaced in the planning report.
   Every item line names a brand.
5. **1st-grade lines may be met from 2nd-grade stock.** Booking walks packed 1st → unpacked 1st →
   packed 2nd → unpacked 2nd, and records what it drew from each source on the order line. Lower grades
   never borrow. The **packed** steps are scoped to the line's brand — another brand's boxes can never
   cover an order — while the **unpacked** steps are shared, because that ware has no brand yet.
   See [schema.md](schema.md) for the full rule.
6. **Dispatch** reduces on-hand **and** reserved together, taking **packed stock first** and falling back
   to unpacked, out of the grades the line actually reserved. You cannot ship more than a line's pending
   quantity, nor more than is physically in stock for that brand. Partial dispatch is allowed and
   advances the status.
7. **Cancelling** an order **releases** the still-reserved (undispatched) quantity, against the same
   sources it was drawn from, in reverse order.
8. Posted production/packing/receipts are **immutable** — mistakes are fixed with a **reversing** entry.
   A production reversal is blocked once the ware has been packed, and a packing reversal once anything
   has shipped for that combination and brand since — so corrections unwind in the order they were applied.
9. Every stock-affecting action writes an **audit row** (who, what, before/after quantity, when).

### Stock is ledger-derived

Every stock change appends an immutable, signed **movement** (`StockMovement` /
`AccessoryStockMovement`); the `StockBalance` rows are a cached running sum that can be **rebuilt from
the ledger at any time** (*Backup → Recalculate balances*, or automatically after a restore). This is
the main defense against double-deduction bugs. The reservation/dispatch/cancel math is covered by the
xUnit suite in `tests/SaniStock.Domain.Tests`.

> **SQLite note:** quantities are stored as `REAL`, not the EF default TEXT-for-decimal, so numeric
> comparisons, ordering and `SUM` behave correctly (a TEXT column would compare `"100" < "20"`).

## Screens

Dashboard · Production & Stock-In (finished, accessory receipt, green ware, raw material) ·
Packing · Stock · Order Booking · Order Dispatch · Reports (Stock, Shortfall/Planning, Production, Orders —
each exportable to PDF and Excel) · Master Data (Admin) · Users (Admin) · Backup (Admin) · About.
