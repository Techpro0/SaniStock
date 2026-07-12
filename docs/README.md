# SaniStock

Offline, installable **Windows desktop app** for a sanitary-ware (ceramics) manufacturer.
It tracks the flow **Production → Stock → Order Booking (reservation) → Order Dispatch (deduction)**,
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
3. seeds grades (1st/2nd/3rd) and a default admin.

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

1. **Production** increases finished on-hand for an item+grade+colour (creating the combination if new).
2. **Order booking** increases *reserved* only — never on-hand — and is **never blocked** by stock.
   A negative `Available (= OnHand − Reserved)` is the shortfall signal, surfaced in the planning report.
3. **Dispatch** reduces on-hand **and** reserved together; you cannot ship more than a line's pending
   quantity. Partial dispatch is allowed and advances the order status.
4. **Cancelling** an order **releases** the still-reserved (undispatched) quantity.
5. Posted production/receipts are **immutable** — mistakes are fixed with a **reversing** entry.
6. Every stock-affecting action writes an **audit row** (who, what, before/after quantity, when).

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
Stock · Order Booking · Order Dispatch · Reports (Stock, Shortfall/Planning, Production, Orders —
each exportable to PDF and Excel) · Master Data (Admin) · Users (Admin) · Backup (Admin) · About.
