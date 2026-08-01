# SaniStock — Project Overview

> Long-term reference document. Everything below was verified by reading the actual source in this
> repository (branch `main`, commit `3e25ef2 "Version 2"` plus uncommitted working-tree changes).
> Where something could not be verified from code, it is explicitly marked
> **[UNVERIFIED]** or **[DISCREPANCY]** rather than guessed at.

---

## 1. PROJECT OVERVIEW

### What SaniStock is

SaniStock is an **offline Windows desktop application** for a **sanitary-ware (ceramics)
manufacturer** — a factory that makes water closets, wash basins, urinals, cisterns and the
associated brass/plastic fittings. It is a shop-floor production and inventory system, not a
general ERP and not accounting software: it tracks **physical pieces**, never money.

### The core problem it solves

A ceramics plant has three facts that generic inventory software handles badly:

1. **Every piece has a quality grade.** Kiln output is sorted into 1st / 2nd / 3rd grade. A customer
   who ordered 1st grade can often be satisfied from 2nd-grade stock, but never the reverse.
2. **"Produced" is not the same as "shippable."** Ware comes out of the kiln unpacked. It cannot go
   on a truck until it has been packed. Both states are real stock, but only one is ready to go.
3. **Brand is decided late.** The same physical piece is sold under several brands, and which one it
   becomes is chosen at *packing* time, not when it is made. A run of 500 might be packed 200 for
   one brand, 200 for another and 100 for a third.

SaniStock models all three. Finished stock is keyed by **Item + Grade + Colour**, and each key's
on-hand quantity is split into an **unpacked** and a **packed** bucket — with packed further split
**per brand**, while unpacked stays one shared, brand-less pool. Orders are placed against a brand
and reserve stock without moving it; dispatch moves it. Anything reserved beyond what exists becomes
a **shortfall**, which drives a "What to Make" production-planning report.

### Target users

Two roles exist in the app (`UserRole` enum in
[Enums.cs](src/SaniStock.Data/Entities/Enums.cs)):

| Role | Who | What they can reach |
|------|-----|---------------------|
| `Operator` | Shop-floor / stores staff | Dashboard, Add Stock, Packing, Stock, New Order, Send Order, Reports, About |
| `Admin` | Supervisor / owner | Everything above, plus Setup Lists (master data), Users, Backup |

Menu filtering is done in [ShellViewModel.cs:48-50](src/SaniStock.App/ViewModels/ShellViewModel.cs#L48-L50).

### The flow, in plain language

```
Raw material / green (unfired) ware   [optional side modules, balance-only]
              │
              ▼
        PRODUCTION  ──►  stock becomes "unpacked"        (+RawOnHand, no brand yet)
              │
              ▼
          PACKING   ──►  moves unpacked → packed         (-RawOnHand, +PackedOnHand, total unchanged)
              │              └─ assigns BRAND; one action may split across several brands
              ▼
     ORDER BOOKING  ──►  raises "Reserved" only          (never blocked; may go negative = shortfall)
              │              ├─ each line names a brand
              │              └─ auto-attaches each item's default accessories
              ▼
    ORDER DISPATCH  ──►  reduces OnHand AND Reserved     (packed stock taken first)
              │
              ▼
       REPORTS / PDF / Excel
```

### Nothing is ever deleted

A standing rule across the whole application: **no user action physically removes a row.** Every
"delete" is a reversible state change that keeps the record and its history intact, and can itself
be undone later.

| What | "Delete" means | Undo |
|------|----------------|------|
| Master data (all 8 Setup Lists) | Clear `IsActive` — the row and everything referring to it stay | Restore |
| Production / Packing / Accessory receipt | A linked reversing entry (`IsReversal`, `ReversesEntryId`) | — (the reversal *is* the undo) |
| Green ware / raw material | A linked reversing entry carrying the **opposite** `IsIssue` | — |
| Orders | `OrderService.Cancel` releases the reservation and sets status `Cancelled` | — |
| Order lines replaced by an edit | Zeroed (`QuantityOrdered = 0`), kept with their allocation history | — |
| Dispatch | A linked reversing note (`IsReversal`, `ReversesEntryId`) that puts the goods back | — |

There is no hard `DELETE` SQL anywhere in the codebase. The one flag `IsActive` carries both
"temporarily switched off" and "deleted"; there is deliberately no second flag.

Two derived numbers drive almost every screen:

```
In Stock (OnHand)  = RawOnHand (not packed) + PackedOnHand (packed, summed over brands)
Free (Available)   = OnHand − Reserved            ← negative means shortfall, not an error
```

Both are read at the **Item + Grade + Colour** level. Brand splits the underlying rows but is never
a grouping the user sees: the Stock screen still shows one row per combination, with the packed
quantity broken out into a column per brand.

---

## 2. TECH STACK

### Language / runtime

| | |
|---|---|
| Language | **C# 12** (`ImplicitUsings` + `Nullable` enabled on every project) |
| Target framework | **.NET 8** — `net8.0` for libraries/tests, `net8.0-windows` for the WPF app |
| Runtime ID | `win-x64`; the shipped installer is a **self-contained** publish (bundles the runtime) |
| Platform | Windows only (WPF). Installer requires Windows 10 build 14393 or later |

### There is no frontend/backend split

This is a **single-process desktop application**. There is no HTTP server, no REST/GraphQL API, no
JavaScript, no `package.json`, no npm/yarn/Docker. All "requests" are in-process method calls.
The app makes **no network calls at all** — it is fully offline by design.

### UI layer ("frontend")

| Concern | Choice |
|---------|--------|
| UI framework | **WPF** (`<UseWPF>true</UseWPF>`), XAML views |
| Pattern | **MVVM** via **CommunityToolkit.Mvvm 8.3.2** (`[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`) |
| "State management" | Per-screen `ViewModelBase` subclasses holding `ObservableCollection<T>`; navigation state in `ShellViewModel` |
| View resolution | Implicit `DataTemplate`s mapping ViewModel type → View, declared in [App.xaml:20-30](src/SaniStock.App/App.xaml#L20-L30) |
| Styling | One hand-written resource dictionary, [Themes/Styles.xaml](src/SaniStock.App/Themes/Styles.xaml) (blue `#1565C0` brand, card/button/label styles). No third-party UI kit |
| DI container | **Microsoft.Extensions.DependencyInjection 8.0.1**, container built in `App.BuildServices()` and exposed as the static `App.Services` |

### Domain / data layer ("backend")

| Concern | Choice |
|---------|--------|
| Business logic | Plain C# service classes in `SaniStock.Domain` (no framework) |
| ORM | **Entity Framework Core 8.0.11** (`Microsoft.EntityFrameworkCore.Sqlite`), code-first with checked-in migrations |
| Unit-of-work | `IDbContextFactory<SaniStockDbContext>` + a custom `DomainScope` (one fresh `DbContext` + all services per user action) |
| Auth | **BCrypt.Net-Next 4.0.3** password hashing against a local `Users` table. No tokens, no OS/AD integration |
| Logging | **Serilog 4.2.0** + `Serilog.Sinks.File` 6.0.0, daily rolling file, 14 files retained |

### Database

| | |
|---|---|
| Engine | **SQLite** (`Microsoft.Data.Sqlite` via the EF Core SQLite provider) |
| File | `%LOCALAPPDATA%\SaniStock\sanistock.db` |
| Hosting | **None — local file, no server.** Single-machine, effectively single-user |
| Schema management | EF Core migrations, applied automatically at startup via `db.Database.Migrate()` |
| Design-time DB | `sanistock_designtime.db`, created in the current working directory by `DesignTimeDbContextFactory` — only used by `dotnet ef` |

**Important storage detail** ([SaniStockDbContext.cs:85-100](src/SaniStock.Data/SaniStockDbContext.cs#L85-L100)):
every `decimal` / `decimal?` property in the entire model is force-converted to SQLite `REAL`
(double) by a global `ValueConverter` loop. EF's default is to store decimals as `TEXT`, which
would make `Reserved > OnHand`, `ORDER BY` and `SUM` compare lexicographically (`"100" < "20"`).
Quantities here are counts, not currency, so double precision is deliberate and sufficient.

### Third-party services / APIs

**None.** Nothing calls out to the internet. The only external-facing dependencies are the NuGet
packages below.

### Reporting / export

| Library | Version | Use |
|---------|---------|-----|
| **QuestPDF** | 2024.12.3 | All PDF reports. Community licence set once at startup by `PdfReports.EnsureLicense()` |
| **ClosedXML** | 0.104.2 | All `.xlsx` exports (generic reflection-based exporter + a hand-laid-out Orders sheet) |

### Testing

| | |
|---|---|
| Framework | **xUnit 2.5.3** (+ `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` 17.8.0, `coverlet.collector` 6.0.0) |
| Style | Integration-style domain tests against a **real in-memory SQLite** connection (`DataSource=:memory:`), *not* the EF in-memory provider |
| Count | 10 test files, **167** `[Fact]`/`[Theory]` cases, all in `tests/SaniStock.Domain.Tests` |

### Build tooling / package management

| | |
|---|---|
| Build | `dotnet build` / MSBuild; solution file `SaniStock.sln` (Visual Studio 2022 format) |
| Packages | NuGet, `PackageReference` style. **No central package management** (`Directory.Packages.props` does not exist) — versions are pinned per-csproj |
| Installer | **Inno Setup 6** script at [installer/SaniStock.iss](installer/SaniStock.iss), compiled with `iscc` |
| CI/CD | **None found.** No `.github/`, no Azure Pipelines, no `.gitlab-ci.yml` |

---

## 3. FOLDER & FILE STRUCTURE

```
SaniStock/
├── SaniStock.sln                       Visual Studio solution — 5 projects (the 2 tools are NOT in it)
├── .gitignore                          Ignores bin/obj/publish/, installer/Output/, *.db, .vs/
├── PROJECT_ANALYSIS_REPORT.md          Pre-existing analysis doc (untracked; not part of the build)
├── PROJECT_OVERVIEW.md                 This file
│
├── .claude/                            Claude Code permission allowlists (settings.json + settings.local.json)
│
├── docs/
│   ├── README.md                       Developer-facing README: build, run, business rules, data locations
│   └── schema.md                       Prose description of the data model and the allocation rules
│
├── installer/
│   ├── SaniStock.iss                   Inno Setup script; also backs up the user's DB before upgrading
│   └── Output/                         Built installer .exe (git-ignored)
│
├── publish/                            Self-contained publish output (git-ignored, ~hundreds of DLLs)
│
├── src/
│   ├── SaniStock.Data/                 ── EF Core layer. No business logic. Depends on nothing internal.
│   │   ├── SaniStockDbContext.cs       DbSets, unique indexes, decimal→REAL conversion, FK delete behaviour
│   │   ├── DbSeeder.cs                 Idempotent first-run seed: 3 grades, 17 product types, 1 brand, default admin
│   │   ├── DesignTimeDbContextFactory.cs   Lets `dotnet ef` build the context without launching the app
│   │   ├── Entities/                   29 POCO entity classes + Enums.cs (see §5)
│   │   └── Migrations/                 5 migrations + model snapshot (see §5 "Migration history")
│   │
│   ├── SaniStock.Domain/               ── All business rules. References SaniStock.Data only.
│   │   ├── DomainException.cs          Business-rule violation → shown to the user as a friendly message
│   │   ├── IUserContext.cs             Who is acting now (username/role), for CreatedBy + audit stamping
│   │   ├── Models/
│   │   │   ├── Inputs.cs               Immutable `record` DTOs for every write operation
│   │   │   └── ReportRows.cs           Immutable `record` DTOs for every read/report shape
│   │   └── Services/                   14 service classes — the entire business layer (see §6)
│   │
│   ├── SaniStock.Reports/              ── PDF/Excel document definitions. References SaniStock.Domain.
│   │   ├── PdfReports.cs               Static facade: rows in → .pdf file out
│   │   ├── ReportLayout.cs             Shared QuestPDF header/footer/cell styling
│   │   ├── ReportColumn.cs             Declarative column definition for generic tables
│   │   ├── TableReportDocument.cs      Generic paginated table report (used by Stock/Production)
│   │   ├── ShortfallReportDocument.cs  "What to Make" — one block per short item + its driving orders
│   │   ├── OrdersReportDocument.cs     Orders — one table per order with an info header block
│   │   └── ExcelReports.cs             Orders .xlsx laid out to mirror the Orders PDF
│   │
│   └── SaniStock.App/                  ── WPF UI. References Domain + Reports. The executable.
│       ├── SaniStock.App.csproj        AssemblyName `SaniStock`, WinExe, net8.0-windows, win-x64
│       ├── App.xaml / App.xaml.cs      Entry point: paths, Serilog, DI, migrate+seed, global handlers, login
│       ├── AssemblyInfo.cs             WPF ThemeInfo attribute (boilerplate)
│       ├── Assets/                     app.ico (window/setup icon), app.png (splash screen)
│       ├── Themes/Styles.xaml          The whole design system: colours, buttons, cards, inputs, DataGrid
│       ├── Infrastructure/
│       │   ├── AppPaths.cs             %LOCALAPPDATA%\SaniStock paths + the SQLite connection string
│       │   ├── DomainScope.cs          Unit-of-work: one DbContext + all 14 services; + IDomainScopeFactory
│       │   ├── IDialogService.cs       Testable wrapper over MessageBox / Open-SaveFileDialog
│       │   ├── ExcelExporter.cs        Reflection-based "any List<T> → .xlsx" exporter
│       │   └── Converters.cs           3 IValueConverters (negative→red, bool→Active, string→Visibility)
│       ├── ViewModels/                 12 screen VMs + ViewModelBase + MasterList<T> helper
│       └── Views/                      11 UserControls + LoginWindow + ShellWindow (XAML + code-behind)
│
├── tests/
│   └── SaniStock.Domain.Tests/         ── xUnit. References SaniStock.Domain.
│       ├── TestHarness.cs              In-memory SQLite + seeded ids + all services wired
│       ├── StockMathTests.cs           Production / booking / dispatch / cancel / reconcile (20 facts)
│       ├── PackingMathTests.cs         The unpacked↔packed bucket rules (16 facts)
│       ├── GradePriorityAllocationTests.cs   Cross-grade borrowing (24 facts)
│       ├── DispatchReversalTests.cs   Undoing a dispatch: buckets, brands, allocations, order (16)
│       ├── OrderEditTests.cs          Editing a booked order: re-allocation, guards, kept history (13)
│       ├── ReversibilityTests.cs      Delete/Restore + reversal for accessories, green ware, raw, recipes, order delete (22)
│       ├── BrandAllocationTests.cs     Brand-scoped packed steps, shared unpacked pool (32 cases)
│       ├── BrandMigrationTests.cs      The AddBrand upgrade path, run through real migrations (12)
│       ├── AccessoryBundlingTests.cs   Item→accessory recipe behaviour (9 facts)
│       └── ReportTests.cs              Shortfall / dashboard / numbering (4 facts)
│
└── tools/                              ── Standalone dev utilities. NOT in SaniStock.sln; never shipped.
    ├── SaniStock.DemoSeeder/           Loads a realistic demo dataset through the domain services
    └── SaniStock.ReportSample/         Renders sample Orders + Stock + Accessory PDFs / PNGs / XLSX from the real DB
```

### Project reference graph

```
SaniStock.App  ──►  SaniStock.Reports  ──►  SaniStock.Domain  ──►  SaniStock.Data
      └───────────────────────────────────────────►┘

SaniStock.Domain.Tests   ──►  SaniStock.Domain
tools/SaniStock.DemoSeeder  ──►  SaniStock.Domain
tools/SaniStock.ReportSample ──►  SaniStock.Reports
```

Strictly layered, no cycles. `SaniStock.Data` depends on nothing internal.

---

## 4. ARCHITECTURE & DATA FLOW

### Design patterns in use

| Pattern | Where |
|---------|-------|
| **Layered architecture** | UI → Domain → Data, enforced by project references |
| **MVVM** | Every screen: `XxxView.xaml` (dumb) + `XxxViewModel.cs` (logic), bound via implicit `DataTemplate` |
| **Unit of Work** | `DomainScope` — one `DbContext` + all services, created and disposed per user action |
| **Abstract Factory** | `IDomainScopeFactory` / `DomainScopeFactory`, injected into every ViewModel |
| **Event sourcing (partial)** | `StockMovement` / `AccessoryStockMovement` are the immutable source of truth; `StockBalance` is a cache rebuildable via `ReconcileAll()` |
| **Facade** | `PdfReports` / `ExcelReports` (static entry points over QuestPDF/ClosedXML documents) |
| **Strategy-ish generic controller** | `MasterList<T>` — one reusable New/Edit/Save controller shared by all 8 master-data tabs |
| **Service Locator (limited)** | `App.Services` static provider, used by `ShellViewModel` for navigation and by window code-behind for login/logout |

### End-to-end flow of one user action

Concrete example: **the user dispatches 30 pieces against an order**.

```
1.  OrderDispatchView.xaml
        Button Command="{Binding DispatchCommand}"
2.  OrderDispatchViewModel.Dispatch()                    [ViewModels/OrderDispatchViewModel.cs:147]
        · parses the typed DispatchQty strings → decimal
        · builds DispatchInput (a record DTO)
        · using var scope = _scopes.Create();            ← fresh DbContext + services
3.  DomainScope.Dispatch → DispatchService.Dispatch(input)
        a. loads the Order with Lines + AccessoryLines
        b. rejects cancelled / already-dispatched orders
        c. folds duplicate line entries together
        d. VALIDATES everything before mutating anything:
             - quantity ≤ line.QuantityPending
             - StockAllocationService.PlanDispatch() decides WHICH GRADE each line draws from
               (read-only; a rejected dispatch leaves allocations untouched)
             - aggregates the per-(item,grade,colour) need
             - rejects if need > physical OnHand  ("Produce more first.")
        e. creates DispatchEntry (+ number from NumberSequenceService) and SaveChanges() to get ids
        f. for each stock key: SplitPackedFirst() → StockService.ApplyFinished(
                 -fromRaw, -fromPacked, -reserved)
               which appends a StockMovement, updates StockBalance, and stages an AuditLog row
        g. marks the OrderLineAllocations as dispatched, advances line + order status
        h. SaveChanges()
4.  back in the ViewModel: IDialogService.Info("Dispatch DN-2026-0004 posted.")
        Load()  → re-reads the open-order list from a NEW scope
5.  scope.Dispose() → DbContext disposed
```

The same shape applies to every write: **ViewModel parses/validates input → builds a record DTO →
opens a `DomainScope` → calls one domain service method → catches `DomainException` and shows it
via `IDialogService` → reloads from a fresh scope.**

### How modules communicate

- **UI → Domain:** direct method calls through `DomainScope`'s public service properties. No
  messaging, no mediator, no events crossing the layer.
- **Domain → Data:** every service takes `SaniStockDbContext` in its constructor. No repository
  interfaces — EF's `DbSet`s *are* the repositories.
- **Domain → Domain:** direct constructor injection. `OrderService` and `DispatchService` both
  depend on `StockService` + `StockAllocationService`; all stock mutation funnels through
  `StockService.ApplyFinished` / `ApplyAccessory`.
- **Screen → Screen:** only through `ShellViewModel.Selected` (the sidebar). `OnSelectedChanged`
  resolves a **fresh transient ViewModel** from DI and calls `OnActivated()`, which reloads data.
  There is no cross-screen messaging, so screens never hold stale data from each other.
- **Logout:** `ShellViewModel` raises the `LogoutRequested` event; `ShellWindow` code-behind handles
  it, calls `UserContext.SignOut()`, and swaps windows.

### Transaction boundaries — and their limits

`StockService` deliberately does **not** save; callers control the boundary and commit with a single
`SaveChanges()` so one business operation is atomic.

**However**, several operations call `SaveChanges()` **twice** — once to obtain an entity id, then
again after posting the movement — and **no explicit database transaction wraps the pair**
(verified: `BeginTransaction` / `TransactionScope` appear nowhere in the repo). Affected:
`ProductionService.Post`, `PackingService.Post`, `AccessoryReceiptService.Post`,
`OrderService.Book`, `DispatchService.Dispatch`, and all the `Reverse` methods. A crash between the
two saves would leave a source document with no matching ledger movement. See §11.

### Concurrency model

Everything runs **synchronously on the WPF UI thread**. There is no `async`/`await`, no background
worker, and no locking. `ViewModelBase.IsBusy` exists but is never set. This is acceptable for a
single-user local SQLite app; it means large report queries will freeze the UI briefly.

---

## 5. DATABASE SCHEMA

26 tables, all created and evolved by EF Core migrations. There are **no stored procedures, no
triggers, and no database views** — SQLite is used purely as a table store, and every rule lives in
C#. The only raw SQL in the codebase is one `INSERT ... SELECT` inside the packing migration.

### Master data

| Table | Key fields | Notes |
|-------|-----------|-------|
| `ProductTypes` | `Code` (unique), `Name`, `IsActive` | Ware category (Water Closet, Wash Basin…). 17 seeded, editable |
| `Items` | `Code` (unique), `Name`, `UnitOfMeasure`(def. `PCS`), `IsActive`, `ProductTypeId?` | Finished product. `ProductTypeId` is nullable, FK `SetNull` on delete |
| `Grades` | `Name` (unique), `SortOrder`, `IsActive` | Seeded 1st/2nd/3rd. **`SortOrder` drives the allocation chain — not the name or id** |
| `Colours` | `Name` (unique), `HexCode?`, `IsActive` | |
| `Brands` | `Code` (unique), `Name`, `IsActive` | The brand ware is packed and sold under. Seeded with one `UNBRANDED` row so packing works on a fresh database |
| `Accessories` | `Code` (unique), `Name`, `UnitOfMeasure`, `IsActive` | Fittings; tracked without grade/colour |
| `ItemAccessoryDefaults` | `ItemId` + `AccessoryId` (unique pair), `QtyPerUnit`(def. 1), `IsActive` | The bundling "recipe". FK: Item `Cascade`, Accessory `Restrict` |
| `Parties` | `Name`, `Address?`, `Contact?`, `Gstin?`, `IsActive` | Customers. **Name is not unique** |
| `RawMaterials` | `Name` (unique), `UnitOfMeasure`(def. `KG`), `IsActive` | |

### Ledgers (immutable, append-only) and their cached balances

| Stock type | Ledger = source of truth | Cached balance | Key |
|------------|--------------------------|----------------|-----|
| Finished goods | `StockMovements` | `StockBalances` | Item + Grade + Colour + **Brand** (unique; brand null = the shared unpacked pool) |
| Accessories | `AccessoryStockMovements` | `AccessoryStockBalances` | Accessory (unique) |
| Green (unfired) ware | `GreenPieceEntries` | `GreenPieceBalances` | Item + Colour (unique) |
| Raw material | `RawMaterialEntries` | `RawMaterialBalances` | RawMaterial (unique) |

**All four are rebuildable by `StockService.ReconcileAll()`.** The first two are signed ledgers
summed per delta column. Green ware and raw material carry direction in an `IsIssue` boolean and
update their balance directly, but since a correction there is now a *mirrored entry* rather than an
edit, their entries genuinely sum to their balance and reconcile covers them too.

#### `StockBalances` — the central table

```csharp
public class StockBalance {
    int Id, ItemId, GradeId, ColourId;
    int? BrandId;                            // null = the shared unpacked pool for that combination
    decimal RawOnHand;                       // produced, NOT yet packed  (always 0 on a branded row)
    decimal PackedOnHand;                    // packed, ready to ship     (always 0 on the null row)
    decimal Reserved;                        // held for booked orders, against THIS row

    decimal OnHand    => RawOnHand + PackedOnHand;   // derived, never stored — Ignore()d
    decimal Available => OnHand - Reserved;          // derived — negative = shortfall
}
```

`OnHand` and `Available` are `Ignore()`d in `OnModelCreating`, so the split and the total can never
drift apart in the database.

**Brand splits the row.** One item+grade+colour spans a brand-less row holding the shared unpacked
pool, plus one row per brand holding that brand's packed stock. Two unique indexes enforce it:
`(ItemId, GradeId, ColourId, BrandId)`, plus a **filtered** unique index on
`(ItemId, GradeId, ColourId) WHERE BrandId IS NULL` — needed because SQLite treats NULLs as distinct,
so the four-column index alone would allow several brand-less rows for one combination.

The `RawOnHand`/`PackedOnHand` invariant above is enforced in **code**, in
`StockService.ApplyFinished`, not by a database check constraint. Balances are a cache that
`ReconcileAll()` rewrites wholesale, and a constraint would turn a ledger anomaly into a crash
during the very repair meant to expose it. The invariant is really a property of the *movements*,
and `ApplyFinished` is the single place they are written.

**Do not show a single row's `Available` to a user.** Dispatch may draw a reservation recorded on
one row from its sibling — packed stock can cover a reservation booked against the pool and vice
versa — so an individual row can read negative while the combination is fully covered. Availability
and shortfall are only ever judged across all of a combination's rows.

#### `StockMovements` — the immutable ledger

```csharp
int Id; DateTime Date;                       // Date = business date (for as-of reports)
int ItemId, GradeId, ColourId;
int? BrandId;                                // null = the shared unpacked pool
StockMovementType Type;
decimal DeltaRawOnHand;                      // signed
decimal DeltaPackedOnHand;                   // signed
decimal DeltaOnHand => DeltaRawOnHand + DeltaPackedOnHand;   // derived — Ignore()d
decimal DeltaReserved;                       // signed
string? SourceType; int? SourceId;           // e.g. "PackingEntry" / 42 — traceability
string CreatedBy; DateTime CreatedAt; string? Remarks;
```

Indexed on `(ItemId, GradeId, ColourId, BrandId, Date)`.

**A movement applies to exactly one balance row.** An action spanning the brand-less pool and a
brand's packed stock therefore writes **two** movements. Packing 200 as Brand A is a `-Raw` leg on
the brand-less key plus a `+Packed` leg on the Brand A key, both typed `Packing` and both pointing
at the same `PackingEntry`. This is what lets `ReconcileAll()` rebuild the per-brand split from the
ledger alone.

**Movement types and their signs** (`StockMovementType`), per movement row:

| Type | RawOnHand | PackedOnHand | Reserved |
|------|-----------|--------------|----------|
| `Production` (0) | **+qty** | 0 | 0 |
| `Reservation` (1) | 0 | 0 | **+qty** |
| `ReservationRelease` (2) | 0 | 0 | **−qty** |
| `Dispatch` (3) | −(remainder) | −(packed first) | **−qty** |
| `Adjustment` (4) | ± (reversal) | ± (reversal) | 0 |
| `Issue` (5) | −qty | 0 | 0 | *(declared for green/raw; not currently written by any service)* |
| `Packing` (6) | **−qty** | **+qty** | 0 → nets to **zero** on the total |

### Source documents

| Table | Key fields | Behaviour |
|-------|-----------|-----------|
| `ProductionEntries` | item+grade+colour, `Quantity`, `IsReversal`, `ReversesEntryId?` | Immutable. Posts `Production` (+Raw). Reversal blocked once the ware has been packed |
| `PackingEntries` | item+grade+colour+**brand**, `Quantity`, `BatchId?`, `IsReversal`, `ReversesEntryId?` | Immutable. Posts `Packing` (two legs). Cannot pack more than is unpacked. **One row per brand**; a multi-brand action posts N rows sharing a `BatchId`. Reversal is per row (per brand), blocked once anything shipped for that combo+brand since — measured by **ledger row id**, not wall-clock |
| `AccessoryReceipts` | accessory, `Quantity`, `IsReversal`, `ReversesEntryId?` | Immutable. Posts accessory `Production` (+OnHand). No packing stage for accessories. Reversal blocked once the stock has shipped, so it cannot drive on-hand negative |
| `GreenPieceEntries` | item+colour, `IsIssue`, `Quantity`, `IsReversal`, `ReversesEntryId?` | In/out of unfired ware (no grade until fired). Immutable; a reversal is a linked entry with the **opposite** `IsIssue`. An issue cannot exceed what is on hand |
| `RawMaterialEntries` | rawMaterial, `IsIssue`, `Quantity`, `IsReversal`, `ReversesEntryId?` | In/out of raw material. Same reversal and non-negative rules as green ware |

### Orders and dispatch

```
Orders (OrderNo unique, PartyId, OrderDate, Status, Remarks, CreatedBy, CreatedAt)
  │  FK PartyId → Parties  [Restrict — deleting a party never cascades away its orders]
  ├── OrderLines            (ItemId, GradeId, ColourId, BrandId, QuantityOrdered/Dispatched/Reserved)
  │     └── OrderLineAllocations  (GradeId, BrandId?, Bucket, Priority, Quantity, QuantityDispatched, QuantityReleased)
  └── OrderAccessoryLines   (AccessoryId, SourceOrderLineId?, QuantityOrdered/Dispatched/Reserved)

DispatchEntries (DispatchNo unique, OrderId, Date, DispatchedBy, Remarks, IsReversal, ReversesEntryId?)
  ├── DispatchLines           (OrderLineId, Quantity)
  └── DispatchAccessoryLines  (OrderAccessoryLineId, Quantity)
```

- `OrderLine.QuantityPending = QuantityOrdered − QuantityDispatched` (derived, `Ignore()`d).
- `OrderAccessoryLine.SourceOrderLineId` links an auto-bundled accessory back to its parent item
  line; **null** for a manually-added standalone accessory. FK is `NoAction` on purpose — `Order`
  already cascade-deletes both collections, and SQLite rejects multiple cascade paths.
- `OrderLineAllocation.QuantityReserved = Quantity − QuantityDispatched − QuantityReleased`
  (derived). FK to `OrderLine` is `Cascade`; FK to `Grade` is `Restrict` (allocation history
  outlives grade edits). Indexed on `(OrderLineId, Priority)`.

#### `OrderLineAllocation` — the cross-grade record

This is the least obvious table in the schema. When a 1st-grade line is booked, the reservation may
be spread across several physical sources. Each source becomes one allocation row:

- **`GradeId` + `BrandId` are authoritative** — together they identify the balance row whose
  `Reserved` was raised, so they are what dispatch deducts from and cancellation releases.
  `BrandId` is **null** for a draw from the shared unpacked pool, and for a shortfall.
- **`Bucket` is advisory only** — a booking-time snapshot (`Packed` / `Raw` / `Shortfall`). Packing
  may legitimately move the quantity between buckets before it ships, so dispatch ignores it.
- **`Priority`** is the draw order (0 = tried first). Dispatch consumes **ascending**; cancellation
  releases **descending**, so the last-borrowed source is handed back first.
- Unlike the ledger, these rows are **mutable** — they track how much has since shipped or been
  released.

**Where the goods come from is decided separately from where the reservation sits.** Dispatch
releases the reservation from the recorded row, but draws the pieces from whichever of the two rows
for that item+grade+colour+**line brand** actually has them — packed first, then the shared pool.
That fallback is what keeps the ordinary flow working: stock booked while unpacked and then packed
under the order's brand is still shippable, and so is stock booked as packed whose packing was later
undone. One physical draw can therefore write two `Dispatch` movements: the `Reserved` leg on the
recorded row, the on-hand leg on the row holding the pieces. **Another brand's packed stock is never
touched** — those pieces are physically in the wrong boxes.

### Grade-priority allocation (the single most important business rule)

Implemented in [StockAllocationService.cs](src/SaniStock.Domain/Services/StockAllocationService.cs).
A **top-grade** line (the lowest `SortOrder` among active grades) is covered by walking four sources
in order, moving on only when one is exhausted:

```
1. packed 1st grade      →  2. unpacked 1st grade  →  3. packed 2nd grade      →  4. unpacked 2nd grade
   FOR THIS LINE'S BRAND     shared, brand-less        FOR THIS LINE'S BRAND       shared, brand-less
```

Anything the four cannot cover becomes a `Shortfall` allocation **against the ordered grade** —
which is what drives that grade's `Available` negative and puts it on the planning report.

Constraints, all covered by tests:
- The fallback is **one-directional and top-grade-only**. 2nd- and 3rd-grade lines reserve solely
  against their own grade.
- Borrowing **never reaches past the 2nd grade** (`ResolveGradeChain` takes at most 2 grades).
- The chain is derived from `SortOrder`, never from grade names or ids, because grades are
  user-maintained master data.
- Because each grade's `Reserved` only counts what was drawn *from that grade*, per-grade
  `Available` already nets out cross-grade reservations — stock is never double-counted.

**Brand narrows the packed steps only.** "Packed 1st grade" means packed 1st grade *for this line's
brand*; another brand's packed pieces can never cover this order. The unpacked steps stay
brand-less, because ware in the pool has not been assigned a brand yet — any order may draw on it
regardless of the brand it was placed for, and whoever packs it later decides. A shortfall carries
no brand either, since producing more lands in the brand-less pool.

### System tables

| Table | Fields |
|-------|--------|
| `Users` | `Username` (unique), `PasswordHash` (BCrypt), `Role` (0=Operator, 1=Admin), `IsActive`, `CreatedAt` |
| `AuditLogs` | `Timestamp`, `Username`, `Action`, `EntityType`, `EntityId?`, `Details` (e.g. `"OnHand 10 -> 8"`), `QuantityBefore?`, `QuantityAfter?` |

### Order status lifecycle

```
Booked (0) ──► PartiallyDispatched (1) ──► Dispatched (2)
     └──────────────► Cancelled (3)
```

Recomputed after every dispatch by `DispatchService.ComputeStatus`: `Dispatched` when all item
**and** accessory lines have zero pending; `PartiallyDispatched` when anything has shipped;
otherwise `Booked`. A fully-dispatched order cannot be cancelled.

### Migration history

| Migration | Date stamp | What it did |
|-----------|-----------|-------------|
| `InitialCreate` | 2026-07-09 | All base tables |
| `AddProductType` | 2026-07-18 | `ProductTypes` table + nullable `Items.ProductTypeId` |
| `AddItemAccessoryBundling` | 2026-07-18 | `ItemAccessoryDefaults` + `OrderAccessoryLines.SourceOrderLineId` |
| `AddPackingAndGradeAllocation` | 2026-07-25 | Packing stage + `OrderLineAllocations`. See below |
| `AddBrand` | 2026-08-01 | Adds `Brands` and splits stock per brand. See below |
| `AddGreenRawReversal` | 2026-08-01 | Two reversal columns each on `GreenPieceEntries` / `RawMaterialEntries`. Purely additive |
| `AddDispatchReversal` | 2026-08-01 | The current release. `IsReversal` / `ReversesEntryId` on `DispatchEntries`. Purely additive; historic dispatches become reversible with no back-fill |

`AddPackingAndGradeAllocation` is worth understanding because it rewrote existing data:

1. Renamed `StockBalances.OnHand` → `RawOnHand` and `StockMovements.DeltaOnHand` → `DeltaRawOnHand`,
   so **all pre-existing stock lands in the unpacked bucket** and a `ReconcileAll()` immediately
   after reproduces identical numbers.
2. Added `PackedOnHand` / `DeltaPackedOnHand` (default 0).
3. Created `PackingEntries` and `OrderLineAllocations`.
4. Ran raw SQL giving **every still-open order line** the allocation row it would have been booked
   with — its own grade, `Bucket = 1 (Raw)`, `Priority = 0`, quantity = what is still reserved:
   ```sql
   INSERT INTO OrderLineAllocations
       (OrderLineId, GradeId, Bucket, Priority, Quantity, QuantityDispatched, QuantityReleased)
   SELECT l.Id, l.GradeId, 1, 0, l.QuantityReserved, 0, 0
   FROM OrderLines l JOIN Orders o ON o.Id = l.OrderId
   WHERE o.Status IN (0, 1) AND l.QuantityReserved > 0;
   ```

Nothing is frozen by putting everything in the unpacked bucket, because dispatch falls back to
unpacked stock when packed is short.

`AddBrand` rewrites existing data too, and its choices are load-bearing:

1. Creates `Brands` and seeds one row, **`UNBRANDED` / "Unbranded"**, left **active** so
   pre-existing packed stock stays visible and shippable. `DbSeeder` seeds the same code on fresh
   databases, so an upgraded install and a new one converge on one brand rather than two similar
   ones; an admin can deactivate it once it has drained.
2. Adds `BrandId` to `StockBalances`, `StockMovements`, `PackingEntries` (required),
   `OrderLines` (required) and `OrderLineAllocations` (nullable), plus `PackingEntries.BatchId`.
3. Assigns every existing packing entry and order line to Unbranded.
4. **Splits each balance**: the original row keeps the unpacked pool and drops its packed quantity;
   a new Unbranded row picks that packed quantity up.
5. **Splits the ledger to match**, so `ReconcileAll()` reproduces step 4 instead of collapsing the
   brands back together. Any historic movement carrying both legs (a packing: `-raw/+packed`)
   becomes two rows — the raw leg stays brand-less, the packed leg moves to Unbranded.
6. **Leaves every pre-existing `Reserved` whole on the brand-less row**, and every back-filled
   allocation with a null `BrandId` — including ones whose `Bucket` was `Packed`.

Step 6 is the subtle one. Splitting reservations packed-first (mirroring how `BucketFree` used to
*deem* them) cannot be expressed in the ledger, because a reservation movement carries no bucket —
so the next `ReconcileAll()` would silently undo it and the cache would disagree with the movements.
Leaving them brand-less is self-consistent, survives reconcile, leaves each combination's total
`Available` unchanged, and still ships correctly: the old lines are Unbranded, the old packed stock
is Unbranded, and a brand-less allocation falls back to its line's brand's packed row. The only
visible effect is that a brand-less row may read negative *on its own* right after upgrade — which
is precisely why availability and shortfall are measured across the whole combination.

One knock-on: step 5 appends the copied packed legs at the end of `StockMovements` with high ids,
which would break `PackingService.Reverse`'s id-monotonic "has anything shipped since?" test for
historic entries. `Reverse` therefore takes the **lowest** movement id for an entry, not the first
one found — conservative, so it can only ever refuse a borderline reversal, never wrongly allow one.

Unusually for this repo, `AddBrand` **is** covered by tests:
[BrandMigrationTests.cs](tests/SaniStock.Domain.Tests/BrandMigrationTests.cs) migrates to the
pre-Brand schema, inserts legacy rows through raw SQL, applies the migration and checks the result —
including that reconcile reproduces it and that a migrated open order can still be dispatched.

---

## 6. CORE FUNCTIONALITY / FEATURES

Twelve screens, listed here in sidebar order (from `ShellViewModel`'s `NavItems`).

### 6.1 Dashboard ("Home")

- **Does:** landing screen. Five counters (today's production qty, today's dispatch count,
  shortfall count, low-stock count, open orders) plus the top 12 shortfalls and top 12 low-stock rows.
- **Files:** [DashboardViewModel.cs](src/SaniStock.App/ViewModels/DashboardViewModel.cs) ·
  [DashboardView.xaml](src/SaniStock.App/Views/DashboardView.xaml)
- **Key calls:** `ReportService.GetDashboard()` → `DashboardSummary`; `GetShortfall().Take(12)`;
  `GetFinishedStock()` filtered by `LowStockThreshold`.

### 6.2 Add Stock ("Production & Stock-In")

- **Does:** four tabs — finished production, accessory receipt, green-ware in/out, raw-material
  in/out. **Each tab now has its own history list of the last 100 entries with an Undo action**,
  so every posting this screen makes can be undone from the same place it was made.
- **Files:** [ProductionViewModel.cs](src/SaniStock.App/ViewModels/ProductionViewModel.cs) ·
  [ProductionView.xaml](src/SaniStock.App/Views/ProductionView.xaml)
- **Domain:** `ProductionService`, `AccessoryReceiptService`, `GreenPieceService`,
  `RawMaterialService` — all four now expose `Post` **and** `Reverse`.
- **Rules:** production lands in **unpacked** only; its reversal is blocked if the unpacked balance
  no longer covers it (i.e. the ware has been packed). An accessory-receipt reversal is blocked once
  the stock has shipped. Green/raw reversals mirror the original entry, and are blocked when
  un-receiving would take the balance below zero; issuing more than is on hand is refused outright.

### 6.3 Packing

- **Does:** moves quantity from unpacked to packed for one item+grade+colour, **assigning a brand**.
  A single action can be split across several brands via a small add/remove list of
  `[Brand ▾] [Qty] [✕]` rows with a running "Total: N of M available" line that turns red when the
  split exceeds what is unpacked. Shows a live "Waiting to be packed / Already packed (all brands)"
  hint as the dropdowns change. History of the last 100 packing rows with per-brand **Undo**.
- **Files:** [PackingViewModel.cs](src/SaniStock.App/ViewModels/PackingViewModel.cs) ·
  [PackingView.xaml](src/SaniStock.App/Views/PackingView.xaml)
- **Domain:** [PackingService.cs](src/SaniStock.Domain/Services/PackingService.cs) — `Post`, `Reverse`
- **Rules:** total stock never changes; the brand lines are checked **together** against what is
  unpacked (all-or-nothing — a rejected split posts none of its lines); no brand twice in one
  action; reversal is per brand row and blocked once anything has shipped for that
  combination **and brand** since.

### 6.4 Stock

- **Does:** two grids (Finished Goods / Accessories) with a free-text filter and a "show short items
  only" toggle. Negative `Free` is painted red. PDF + Excel export.
- **Files:** [StockViewModel.cs](src/SaniStock.App/ViewModels/StockViewModel.cs) ·
  [StockView.xaml](src/SaniStock.App/Views/StockView.xaml)
- **Columns:** Code · Item · Grade · Colour · **Not Packed** · *one **Packed** column per brand* ·
  **Packed Total** · **In Stock** · **Booked** · **Free**
- **One row per Item+Grade+Colour.** Brand is extra columns across the row, never extra rows.
  "Not Packed" stays a single shared number, because unpacked ware has no brand yet.
- **Dynamic columns.** WPF cannot declare a variable column count in XAML, so
  [StockView.xaml.cs](src/SaniStock.App/Views/StockView.xaml.cs) splices them in after "Not Packed"
  whenever the view model raises `BrandColumnsChanged`. Each row's `PackedByBrand` list is built
  **index-aligned** to `BrandColumns`, so the grid, the PDF and the spreadsheet all address the
  breakdown by position (`PackedByBrand[i].Packed`).
- **Which brands get a column:** every active brand, **plus** any deactivated brand still holding
  packed stock — otherwise deactivating a brand would quietly hide real, shippable goods.
- **The export buttons follow the visible tab.** The Accessories tab's `IsSelected` is bound two-way
  to `StockViewModel.ShowingAccessories`, which is what the toolbar buttons read — they sit above
  the `TabControl`, so without it they have no idea which grid the user means. The file name follows
  too (`FinishedStock_…` / `AccessoryStock_…`), so the two cannot overwrite each other, and the
  confirmation message names what was saved.
- **Which writer each grid uses:** finished goods go through the brand-aware
  `PdfReports.SaveFinishedStock` / `ExcelReports.SaveFinishedStock`, because the generic
  reflection-based `ExcelExporter` would silently drop the per-brand breakdown. Accessories have no
  brand and only simple properties, so they use `PdfReports.SaveAccessoryStock` and the generic
  exporter. The finished PDF turns landscape once there are more than 3 brands.

### 6.5 New Order (Order Booking)

- **Does:** stage item lines and standalone accessory lines, then book. When an item line is added,
  its default accessories are auto-populated at `QtyPerUnit × qty`, each with a per-line
  include/exclude checkbox. Shows the 50 most recent orders.
- **Files:** [OrderBookingViewModel.cs](src/SaniStock.App/ViewModels/OrderBookingViewModel.cs)
  (contains `BookingLine`, `BookingLineAccessory`, `BookingAccessoryLine`, `RecentOrderRow`) ·
  [OrderBookingView.xaml](src/SaniStock.App/Views/OrderBookingView.xaml)
- **Domain:** [OrderService.Book](src/SaniStock.Domain/Services/OrderService.cs#L40)
- **Delete an order:** the Recent Orders list has a **Delete** button per row. It calls
  `OrderService.Cancel` — the order stays on record with status `Cancelled`, its lines and
  allocations intact, and every reservation it still held is released back to the exact grade+brand
  rows it drew from. Anything already dispatched is untouched. The button is hidden once an order is
  cancelled or fully sent, since there is nothing left to release.
- **Rules:** every item line names a **Brand** alongside item/grade/colour. Booking raises `Reserved`
  only, never `OnHand`, and is **never blocked** by a lack of stock. Lines are allocated **one at a
  time**, applying each reservation before planning the next, so two lines competing for the same
  stock see each other's claims — though two lines for *different* brands only compete over the
  shared unpacked pool, never over each other's packed stock. The brand dropdown offers **active
  brands only**.

### 6.6 Orders (list · view · edit · delete)

- **Does:** every order taken, not just the 50 recent ones *New Order* shows. Filters for status,
  customer, brand and date range; a master-detail layout with the list on the left and the opened
  order on the right.
- **Files:** [OrdersViewModel.cs](src/SaniStock.App/ViewModels/OrdersViewModel.cs) ·
  [OrdersView.xaml](src/SaniStock.App/Views/OrdersView.xaml)
- **Detail view:** item lines (brand/grade/colour, ordered/sent/left), accessory lines showing which
  item line each was bundled with, and the order's dispatch history. An expander shows **the stock
  each line is actually holding** — the grade+brand rows behind the reservation — which is what
  explains a shortfall, since the grade holding the stock is not always the one printed on the line.
- **Edit / Delete** call `OrderService.Edit` / `OrderService.Cancel`; neither reimplements anything.
  Each button is hidden when its rule does not allow it — Edit only while nothing has shipped,
  Delete only while there is a reservation left to release.
- **Undo** on the dispatch history calls `DispatchService.Reverse`, and appears only on the newest
  still-standing note. A reversing note shows a **negative** quantity and is labelled `Reversal`;
  the note it undid is labelled `Reversed`.
- **Separate from *New Order*,** which stays a booking-only workflow.
- **Row cap:** the grid virtualises rendering but the query does not, so it fetches at most **500**
  rows and says so when it hits the cap rather than silently truncating.

### 6.7 Send Order (Order Dispatch)

- **Does:** pick an open order, see its lines with bundled accessories nested beneath their parent
  item line, type quantities (or hit "dispatch all pending"), and post.
- **Files:** [OrderDispatchViewModel.cs](src/SaniStock.App/ViewModels/OrderDispatchViewModel.cs) ·
  [OrderDispatchView.xaml](src/SaniStock.App/Views/OrderDispatchView.xaml)
- **Domain:** [DispatchService.Dispatch](src/SaniStock.Domain/Services/DispatchService.cs#L35)
- **Rules:** cannot exceed a line's pending quantity, nor physical stock. Draws from the grades the
  line actually reserved, packed-first within each grade — where "packed" means *this line's
  brand's* packed stock, with the shared unpacked pool as the fallback. Partial dispatch is allowed
  and advances the status. Lines are labelled `Item / Grade / Colour / Brand`, since the same
  combination can appear twice on one order under two brands.

### 6.8 Reports

- **Does:** four tabs — Stock, **What to Make** (shortfall), Production (date-ranged, item filter),
  Orders (date-ranged, party filter) — each with export buttons.
- **Files:** [ReportsViewModel.cs](src/SaniStock.App/ViewModels/ReportsViewModel.cs) ·
  [ReportsView.xaml](src/SaniStock.App/Views/ReportsView.xaml) · the whole `SaniStock.Reports` project
- **Export matrix (as wired in the UI):**

  | Report | PDF | Excel |
  |--------|-----|-------|
  | Stock | ✅ `PdfReports.SaveFinishedStock` | ✅ generic `ExcelExporter` |
  | What to Make | ✅ `PdfReports.SaveShortfall` | ❌ **not wired** |
  | Production | ✅ `PdfReports.SaveProduction` | ✅ generic `ExcelExporter` |
  | Orders | ✅ `PdfReports.SaveOrders` | ✅ bespoke `ExcelReports.SaveOrders` |

### 6.9 Setup Lists (Master Data) — **Admin only**

- **Does:** eight CRUD tabs (Product Type, Items, Grades, Colours, **Brands**, Accessories,
  Customers, Raw Materials). The Items tab additionally hosts the **default-accessory recipe
  editor**: a checklist of every active accessory with a qty-per-unit box and a master "include
  accessories by default" toggle.
- **Files:** [MasterDataViewModel.cs](src/SaniStock.App/ViewModels/MasterDataViewModel.cs) ·
  [MasterList.cs](src/SaniStock.App/ViewModels/MasterList.cs) ·
  [MasterDataView.xaml](src/SaniStock.App/Views/MasterDataView.xaml)
- **Domain:** [MasterDataService.cs](src/SaniStock.Domain/Services/MasterDataService.cs)
- **Delete / Restore.** Each row has **Edit · Delete** (active) or **Edit · Restore** (deleted), and
  a **Show deleted** toggle above each grid, on by default — hiding deleted rows would make a
  deletion look permanent and leave no route back. Delete asks for confirmation and says plainly
  that nothing is erased. Underneath it is only `IsActive`; see "Nothing is ever deleted" in §1.
- **One change, not eight.** All 8 tabs share one `MasterList<T>` and one `RowActionsCell`
  template. `MasterList<T>` is constrained to
  [`IActivatable`](src/SaniStock.Data/Entities/IActivatable.cs), so Delete/Restore is compile-checked
  rather than reflective, and it takes `IDialogService` + a label ("Colour") to build its own prompts.
  Delete clones the row, flips the flag and saves through the tab's normal `save` delegate, so
  per-entity validation still runs and a refused save leaves the grid untouched.
- **Grades are guarded.** `MasterDataService.SaveGrade` refuses to switch off the **last active
  grade** — every stock row and order line must name one, and `ResolveGradeChain` derives the
  borrowing chain from the active grades. Deleting a grade while others remain is allowed, but note
  it silently re-points that chain, since the chain is always "the two lowest sort orders still
  active".

### 6.10 Users — **Admin only**

- **Does:** list users, create a user (username + password + role), reset a password, toggle active.
- **Files:** [UserManagementViewModel.cs](src/SaniStock.App/ViewModels/UserManagementViewModel.cs) ·
  [UserManagementView.xaml](src/SaniStock.App/Views/UserManagementView.xaml) (+ code-behind, because
  WPF's `PasswordBox.Password` is not bindable)
- **Domain:** [AuthService.cs](src/SaniStock.Domain/Services/AuthService.cs)
- **Rule:** the last active administrator cannot be deactivated.

### 6.11 Backup — **Admin only**

- **Does:** three actions — *Backup now* (copy the .db somewhere), *Restore* (replace the live .db,
  keeping a `.bak`, then auto-reconcile), *Recalculate balances* (`ReconcileAll` on demand).
- **Files:** [BackupViewModel.cs](src/SaniStock.App/ViewModels/BackupViewModel.cs) ·
  [BackupView.xaml](src/SaniStock.App/Views/BackupView.xaml)
- **Domain:** [BackupService.cs](src/SaniStock.Domain/Services/BackupService.cs)
- **Detail:** calls `SqliteConnection.ClearAllPools()` before copying so the file isn't locked.
  After a restore the user is told to close and reopen the app.

### 6.12 About

Static info card: app name, tagline, assembly version, framework string, and the data folder path.

### Cross-cutting: the audit trail

Every stock-affecting action stages an `AuditLog` row inside the same `SaveChanges()` as the change
itself (`StockService.AddAudit`), recording who, what, the entity, a human-readable before/after
string, and the numeric before/after. `AuthService` also audits user create / password reset /
activate / deactivate. **There is no UI anywhere that displays audit rows** — they are read by
querying the database directly.

---

## 7. KEY FILES DEEP-DIVE

Ordered roughly by how much you need to understand them before changing anything.

### 1. `src/SaniStock.Domain/Services/StockService.cs` (243 lines)

**The single choke-point for every stock change.** If you are touching stock, you go through here.

- `ApplyFinished(itemId, gradeId, colourId, brandId, type, ΔrawOnHand, ΔpackedOnHand, Δreserved, date, sourceType, sourceId, remarks)`
  — appends a `StockMovement`, applies the deltas to the cached `StockBalance`, and stages an
  `AuditLog`. **Does not save** — the caller owns the transaction boundary. One call touches
  **one** balance row; an action spanning the pool and a brand makes two calls.
- `ApplyAccessory(...)` — the accessory analogue. Accessories have no brand (and no packing stage).
- `FindFinishedBalance(item, grade, colour, brandId = null)` — change-tracker-first lookup returning
  `null` when that row has never been stocked. Use this for read-only validation so a failed check
  doesn't leave an empty balance row staged.
- `GetOrCreateFinishedBalance(...)` / `GetOrCreateAccessoryBalance(...)`.
- `static FreeOn(bal) → decimal` — unreserved quantity on one row. Since brand puts each bucket on
  its own row along with exactly the reservations drawn from it, this is a plain subtraction. It
  replaced the old `BucketFree`, which had to *deem* reservations packed-first to guess the split
  out of a single combined row.
- `AddAudit(action, entityType, entityId, details, before, after)`.
- `ReconcileAll()` — rebuilds every cached balance as `GROUP BY` sums over the ledgers. Each bucket
  rebuilds from its own signed delta, so no knowledge of packing or allocation order is needed.
  Returns the number of balance rows touched.

### 2. `src/SaniStock.Domain/Services/StockAllocationService.cs` (167 lines)

Decides **which physical stock covers a booked line**, and how it is later consumed.

- `record AllocationStep(GradeId, BrandId?, Bucket, Quantity, Priority)` — one planned source.
- `record AllocationDraw(Allocation?, GradeId, BrandId?, Quantity)` — one planned draw against a
  recorded allocation; `Allocation` is `null` for the legacy fallback.
- `record PhysicalDraw(FromPacked, FromRaw)` — how a shipment actually leaves the two rows.
- `ResolveGradeChain(gradeId) → List<int>` — returns 2 grades only for the top grade, else 1.
- `Plan(itemId, gradeId, colourId, brandId, quantity) → List<AllocationStep>` — the four-source
  walk, packed steps scoped to `brandId` and unpacked steps brand-less; the steps always sum to
  `quantity`, with a trailing `Shortfall` step if stock ran out.
- `PlanDispatch(line, qty)` / `PlanRelease(line, qty)` — ascending / descending by `Priority`.
  Both are **read-only**, so an operation can be fully validated before anything mutates.
- `static MarkDispatched(draws)` / `static MarkReleased(draws)` — commit the plan onto the rows.
- `static BySource(draws)` — totals draws per source **row** (grade + brand), preserving first-drawn
  order. This is what cancellation releases against.
- `static PlanPhysicalDraw(quantity, rawAvailable, packedAvailable)` — pure; splits a shipment
  packed-first across the brand's packed row and the shared pool. Brand does not change the
  preference, only which two rows the caller passes in.

### 3. `src/SaniStock.Domain/Services/OrderService.cs` (229 lines)

- `Book(OrderInput) → Order` — validates (at least one line, all quantities > 0, party exists),
  creates the order and item lines, saves to get ids, **auto-attaches each line's default
  accessories** minus per-line exclusions, then allocates line by line and writes one `Reservation`
  movement **per grade touched**.
- `Cancel(orderId, reason?)` — releases still-reserved quantity on every line against the exact
  recorded sources in reverse draw order, plus all accessory lines; sets status `Cancelled`.
  **Blocked** for already-cancelled and fully-dispatched orders. This is the app's "delete an order".
- `Edit(orderId, OrderInput)` — rewrites an order's lines under the **same `Order` and `OrderNo`**.
  **Blocked** once anything on the order has shipped (item *or* accessory line), and for cancelled
  orders. Mechanically it is Cancel-then-Book on one order: `ReleaseEverything` hands the whole
  reservation back, the superseded lines are set to quantity 0 (kept, with their allocations), and
  the new lines go through `AddLinesAndReserve`.
- `AddLinesAndReserve(order, lines, accessoryLines)` / `ReleaseEverything(order, remark)` — the two
  halves shared by `Book`, `Edit` and `Cancel`. **There is deliberately no second reservation path**:
  an edited order allocates through exactly the code a freshly booked one does, so the two cannot
  drift apart.

> Because an edit zeroes superseded lines rather than removing them, every read path that lists
> order lines filters `QuantityOrdered > 0` — `ReportService.GetOrders`, the dispatch picker, and
> the Orders screen. `GetShortfall` already filtered `QuantityOrdered > QuantityDispatched`, which
> excludes them for free.
- `DescribeAllocation(...)` — builds the human ledger remark, e.g.
  `"Booking ORD-2026-0007 (packed 50 for Brand A) — covering a 1st grade line"`.

### 4. `src/SaniStock.Domain/Services/DispatchService.cs` (205 lines)

- `Dispatch(DispatchInput) → DispatchEntry` — the longest method in the codebase, and deliberately
  ordered: **fold duplicates → validate everything → plan (read-only) → aggregate per stock key →
  check physical stock → create the note → move stock → advance statuses**. Brand added no new
  business rule here, but it did split the bookkeeping: the private `StockNeed` record tracks how
  much of each key was reserved *against the pool* versus *against the brand*, because the goods may
  physically come out of the other row.
  <br>**The shared pool is rationed across keys.** A brand's packed row belongs to exactly one
  dispatch key, but two keys differing only in brand draw on the *same* unpacked pieces. The
  planning loop therefore carries a running `poolLeft` per item+grade+colour; without it, two brands
  on one order would each validate against the whole pool and together ship stock that does not
  exist. Booking needs no equivalent, because it applies each line's reservation before planning the
  next, so the next `Plan` already sees the claim.
- `Reverse(dispatchEntryId, remarks?)` — puts the goods back in the exact buckets and brand rows
  they left from, re-raises the reservation, un-consumes the order lines and their allocations,
  recomputes the status, and writes a linked reversing note. See below.
- `static ComputeStatus(order)` — `Dispatched` / `PartiallyDispatched` / `Booked`.
- `DescribeFinished(...)` — "Item / Grade / Colour" for error messages.

#### Reversing a dispatch

**Where the goods came from is read back from the ledger, not from a second record.** A shipped
line can span several grades (cross-grade borrowing) and, within each, a brand's packed stock and
the shared unpacked pool — so a per-`DispatchLine` "from packed / from raw" pair cannot express it,
and would be a lossy duplicate of something the app already stores. Every dispatch already writes
`StockMovement` rows carrying the exact item+grade+colour+**brand** and the raw/packed/reserved
split, and those rows are what `ReconcileAll()` rebuilds balances from. `Reverse` loads them by
`SourceType = "DispatchEntry"` + `SourceId` and negates them, so the restore is correct by
construction rather than by agreement between two records that could drift.

**Only the newest still-standing dispatch on an order can be reversed.** Restoring the allocations
means walking them in **descending** priority and un-consuming — the exact inverse of the ascending
walk `MarkDispatched` used. That inverse is only the true one for the last dispatch: reversing an
earlier one out of turn would hand quantity back to whichever allocations the *later* dispatch
happened to take, silently corrupting which source each line holds. Unwind newest-first instead —
the same discipline production and packing reversals already follow. "Still standing" means a note
that is neither a reversal nor itself reversed, so reversing the newest makes the one before it
reversible in turn.

The reversal itself is always safe on stock: it only ever adds on-hand back and re-raises Reserved,
neither of which can go negative. A reversed order also becomes editable and cancellable again,
which is how a fully-dispatched order gets corrected.

### 5. `src/SaniStock.Data/SaniStockDbContext.cs` (145 lines)

25 `DbSet`s plus all model configuration in one `OnModelCreating`: unique business keys, one-row-per-
key balance indexes, ledger query indexes, the `Ignore()` list for derived properties, the global
**decimal → REAL** value-converter loop, and the four hand-set FK delete behaviours (`Order→Party`
Restrict, `Item→ProductType` SetNull, `OrderAccessoryLine→SourceOrderLine` NoAction,
`OrderLineAllocation→Grade` Restrict).

### 6. `src/SaniStock.Domain/Services/ReportService.cs` (215 lines)

All read-only queries. `LowStockThreshold` (default `10m`) is a mutable property, not persisted.

- `GetFinishedStock(includeZero)` → `FinishedStockView` — the brand columns plus one row per
  item+grade+colour, folding the per-brand balance rows back together with the packed quantity
  broken out index-aligned to those columns.
- `GetStockBrandColumns()` → active brands **plus** inactive brands still holding packed stock.
- `GetAccessoryStock(includeZero)` → `List<AccessoryStockRow>`
- `GetShortfall()` → `List<ShortfallRow>` — the planning report. Attributes each driving order to
  **the grade whose stock it actually holds**, not the grade printed on the line; otherwise a
  2nd-grade shortfall caused by a 1st-grade order would appear with no orders explaining it.
  Brand appears as **context on the drivers only**: the report stays keyed by item+grade+colour and
  measured on the combination's totals, because production lands in the brand-less pool and there is
  nothing you can "produce for Brand A". See §11 for the blind spot that creates.
- `GetProduction(from, to, itemId?)`, `GetOrders(from, to, partyId?)` (item lines ∪ accessory lines)
- `GetDashboard()` → `DashboardSummary`

### 7. `src/SaniStock.App/App.xaml.cs` (124 lines)

The composition root and the whole startup sequence:
`EnsureDirectories` → configure Serilog → `PdfReports.EnsureLicense()` → register global exception
handlers → `BuildServices()` → `Migrate()` + `DbSeeder.EnsureSeeded()` (fatal dialog + shutdown on
failure) → show `LoginWindow`. Also `OnExit` → `Log.CloseAndFlush()`.

Registered in DI: `UserContext` (singleton, also as `IUserContext`), the pooled
`IDbContextFactory<SaniStockDbContext>`, `IDomainScopeFactory` and `IDialogService` (singletons),
and every ViewModel/Window as **transient** so each navigation reloads cleanly. Note that **no
domain service is registered in DI** — they are all constructed by `DomainScope`.

### 8. `src/SaniStock.App/Infrastructure/DomainScope.cs` (79 lines)

`DomainScope : IDisposable` — one `SaniStockDbContext` plus all 14 domain services wired to it,
exposed as public properties (`Stock`, `Allocations`, `Numbers`, `Production`, `Packing`,
`AccessoryReceipts`, `Orders`, `Dispatch`, `Reports`, `Green`, `Raw`, `Master`, `Auth`, `Db`).
`IDomainScopeFactory.Create()` is what every ViewModel actually holds. **The idiom to follow is
`using var scope = _scopes.Create();` per action** — never cache a scope on a ViewModel.

### 9. `src/SaniStock.Domain/Services/PackingService.cs` (137 lines)

`Post(PackingInput)` and `Reverse(packingEntryId, remarks?)`. The interesting part of `Reverse` is
how "has anything shipped since?" is determined — by comparing **`StockMovement.Id`**, not
timestamps, because ids are monotonic and two postings can share a clock tick:

```csharp
var packingMovementId = _db.StockMovements
    .Where(m => m.SourceType == "PackingEntry" && m.SourceId == original.Id)
    .Select(m => (int?)m.Id).FirstOrDefault() ?? 0;

var dispatchedSince = _db.StockMovements.Any(m =>
    m.ItemId == ... && m.GradeId == ... && m.ColourId == ... &&
    m.Type == StockMovementType.Dispatch && m.Id > packingMovementId);
```

### 10. `src/SaniStock.Data/Entities/StockBalance.cs` + `StockMovement.cs` + `OrderLineAllocation.cs`

The three entities that encode the whole model. Their XML doc comments are the best short
explanation of the design in the repo — read them before changing stock math.

### 11. `src/SaniStock.App/ViewModels/ShellViewModel.cs` (65 lines)

Navigation. `record NavItem(Icon, Title, ViewModelType, AdminOnly)`; the array of 12 items is
filtered by role in the constructor. `OnSelectedChanged` resolves a fresh ViewModel from
`App.Services` and calls `OnActivated()`. Raises `LogoutRequested`, handled by `ShellWindow`.

### 12. `src/SaniStock.App/ViewModels/MasterList.cs` (59 lines)

`MasterList<T>` — a generic list+edit-form controller taking five delegates (`load`, `save`,
`clone`, `factory`, `onError`) and exposing `Items`, `Editing`, and `New`/`Edit`/`Save` commands.
All eight master-data tabs are instances of this, which is why `MasterDataViewModel` is short.
Note the **clone-on-edit** pattern: editing works on a copy, so cancelling leaves the grid untouched.

### 13. `src/SaniStock.Reports/PdfReports.cs` (74 lines)

Static facade. `EnsureLicense()`, `SaveFinishedStock`, `SaveAccessoryStock`, `SaveShortfall`,
`SaveProduction`, `SaveOrders`. The first two/three build a `List<ReportColumn<T>>` and hand it to
the generic `TableReportDocument<T>`; shortfall and orders use bespoke document classes.

### 14. `src/SaniStock.Data/DbSeeder.cs` (81 lines)

`EnsureSeeded(db)` — idempotent, called at every startup after migrations. Seeds three grades
(1st/2nd/3rd with `SortOrder` 1/2/3), 17 product types **by code** so admin edits are never
overwritten, and — only if the `Users` table is empty — the default admin.
`DefaultAdminUsername = "admin"`, `DefaultAdminPassword = "admin123"` are `public const`.

### 15. `tests/SaniStock.Domain.Tests/TestHarness.cs` (138 lines)

`IDisposable` fixture creating a `SqliteConnection("DataSource=:memory:")`, calling
`EnsureCreated()` (**note: not `Migrate()`** — tests build the schema from the model, not the
migrations), wiring all services, and seeding ids exposed as properties (`ItemA`, `Grade1`, `White`,
`Acc1`, `PartyX`, …). Helper assertions: `FinishedBalance`, `FinishedBuckets`, `Allocation`,
`AccessoryBalance`. Any new domain test should start from this.

---

## 8. AUTH & SECURITY

### Authentication

- **Local username + password only.** Table `Users`, hashed with **BCrypt** (`BCrypt.Net-Next`,
  default work factor — not configured explicitly).
- `AuthService.Authenticate(username, password)` returns the `User` or `null`. **Inactive users
  cannot log in.** Blank username or empty password short-circuits to `null`.
- Flow: `LoginWindow` (code-behind reads `PasswordBox.Password`, which WPF does not allow binding)
  → `LoginViewModel.TryLogin(password)` → on success `UserContext.SignIn(user)` → `ShellWindow`
  opens, `LoginWindow` closes.
- **Default credentials: `admin` / `admin123`**, seeded on first run. `docs/README.md` tells the
  user to change it after first sign-in, but **nothing in the app enforces or prompts for that**.

### Authorization

There is exactly **one** authorization mechanism: the `AdminOnly` flag on `NavItem`, filtered in
`ShellViewModel`'s constructor. Admin-only screens are Setup Lists, Users and Backup.

**There are no guards below the UI.** No domain service checks `IUserContext.Role` — verified by
searching for `Role` across `SaniStock.Domain`; the only role check in the whole codebase is
`AuthService.SetActive`'s "last active administrator" rule. `UserContext` is used *only* for
stamping `CreatedBy` and `AuditLog.Username`.

This is a defensible choice for a single-user offline desktop app on a trusted machine — the
security boundary is the Windows login, not the app. It does mean **any code path that bypasses the
sidebar bypasses authorization entirely**.

### Session model

`UserContext` is a **mutable singleton** with `Username`, `Role`, `IsAuthenticated` and
`SignIn`/`SignOut`. There is no expiry, no idle timeout, no lockout after failed attempts, and no
re-authentication for sensitive actions. Signing out calls `SignOut()` and swaps back to
`LoginWindow`.

### Password policy

Minimum **4 characters** (`AuthService.CreateUser` and `ResetPassword`). No complexity rules, no
history, no expiry, no rate limiting.

### Data-at-rest

The SQLite file is **not encrypted** and carries no password. Anyone with read access to
`%LOCALAPPDATA%\SaniStock\sanistock.db` can read every table (password hashes included, though
BCrypt protects those) and can modify data outside the app entirely. Backups are plain file copies
with the same exposure.

### Input handling

All database access goes through EF Core with parameterised LINQ, so SQL injection is not a
practical concern. The single piece of raw SQL (in the packing migration) is a static string with no
user input. Business validation lives in the domain services and surfaces as `DomainException`,
which the ViewModels catch and show through `IDialogService.Error`.

### Error handling and logging

`App.RegisterGlobalExceptionHandlers()` wires three handlers: `DispatcherUnhandledException` (logs,
shows a warning dialog, marks handled), `AppDomain.UnhandledException` (logs fatal), and
`TaskScheduler.UnobservedTaskException` (logs, marks observed). Logs go to
`%LOCALAPPDATA%\SaniStock\logs\log-*.txt`, daily rolling, 14 files retained, minimum level
`Information`. Logs contain usernames and quantities but no passwords.

---

## 9. CONFIGURATION & ENVIRONMENT

### Environment variables

**None.** The application does not read a single environment variable of its own. Verified by
searching for `Environment.GetEnvironmentVariable` across the repo — the only `Environment` usage is
`Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` in `AppPaths` and in the two tools.

### Configuration files

**There is no `appsettings.json`, no `.env`, no user-settings file, and no registry usage.**
All configuration is compile-time constants. This is the complete list of what a maintainer might
otherwise expect to find in config:

| Setting | Value | Where it lives |
|---------|-------|----------------|
| Data root | `%LOCALAPPDATA%\SaniStock` | `AppPaths.RootDir` |
| Database file | `<root>\sanistock.db` | `AppPaths.DbPath` |
| Connection string | `Data Source=<DbPath>` | `AppPaths.ConnectionString` |
| Log directory | `<root>\logs` | `AppPaths.LogDir` |
| Log level / rolling / retention | `Information`, daily, 14 files | `App.OnStartup` |
| Low-stock threshold | `10` | `ReportService.LowStockThreshold` (settable property, never set by any caller) |
| Default admin | `admin` / `admin123` | `DbSeeder` public consts |
| Minimum password length | 4 | `AuthService` |
| Order number format | `ORD-{year}-{0000}` | `NumberSequenceService.NextOrderNo` |
| Dispatch number format | `DN-{year}-{0000}` | `NumberSequenceService.NextDispatchNo` |
| Report company name | `"SaniStock — Sanitary Ware Works"` | `ReportLayout.CompanyName` |
| Brand colour | `#1565C0` | `Themes/Styles.xaml` |
| QuestPDF licence | `LicenseType.Community` | `PdfReports.EnsureLicense()` |
| Design-time DB | `Data Source=sanistock_designtime.db` (CWD) | `DesignTimeDbContextFactory` |

> **Note:** `docs/schema.md` describes reservations as consuming packed stock first, and `Order`
> numbers as `SO-2026-0007` in one example remark. The code actually emits the prefix **`ORD-`**
> (`NumberSequenceService.NextOrderNo`). The doc's `SO-` is illustrative prose, not a second format.

### Build configuration

| Property | Value | File |
|----------|-------|------|
| `AssemblyName` | `SaniStock` | `SaniStock.App.csproj` |
| `Version` | `1.0.0` | `SaniStock.App.csproj` |
| `RuntimeIdentifiers` | `win-x64` | `SaniStock.App.csproj` |
| `ApplicationIcon` | `Assets\app.ico` | `SaniStock.App.csproj` |
| Installer `AppVersion` | `2.2.0` | `installer/SaniStock.iss` |
| Installer `MinVersion` | `10.0.14393` (Windows 10 1607) | `installer/SaniStock.iss` |

### `.claude/` settings

Two permission allowlists for Claude Code (`settings.json` tracked, `settings.local.json` local).
They permit `dotnet build/test/ef/tool/run`, `python`, and a few file operations. They have **no
effect on the application** — purely developer tooling.

---

## 10. SETUP & RUN INSTRUCTIONS

### Prerequisites

- **.NET 8 SDK** (Windows). WPF means the build only works on Windows.
- **Inno Setup 6** — only if you want to build the installer.
- `dotnet-ef` CLI tool — only if you need to add migrations:
  ```bash
  dotnet tool install --global dotnet-ef --version 8.*
  ```

### Restore, build, test

```bash
# from the repo root
dotnet restore
dotnet build                 # builds all 5 projects in SaniStock.sln
dotnet test                  # runs the ~167 xUnit domain tests
```

> **[DISCREPANCY]** `docs/README.md` refers to a solution file named `SaniStock.slnx`. The file in
> this repo is **`SaniStock.sln`** — there is no `.slnx`. Bare `dotnet build` works either way
> because it discovers the single solution in the directory.

### Run locally

```bash
dotnet run --project src/SaniStock.App
```

On first run the app will:
1. create `%LOCALAPPDATA%\SaniStock\` and its `logs\` subfolder,
2. apply all EF Core migrations to `sanistock.db` (creating it if absent),
3. seed 3 grades, 17 product types, the Unbranded brand and the default admin,
4. show the login window.

**Sign in with `admin` / `admin123`**, then change it from *Users*.

### Migrations

Migrations are applied **automatically at every startup** by `db.Database.Migrate()` — you never run
them by hand for normal use. To author a new one:

```bash
dotnet ef migrations add <Name> --project src/SaniStock.Data --startup-project src/SaniStock.Data
dotnet ef database update  --project src/SaniStock.Data --startup-project src/SaniStock.Data
```

`DesignTimeDbContextFactory` supplies the connection, so `SaniStock.Data` can act as its own startup
project. It writes `sanistock_designtime.db` into the current working directory — a scratch file,
safe to delete (and git-ignored by the `*.db` rule).

> Tests use `EnsureCreated()`, not `Migrate()`. A green test suite therefore does **not** prove that
> the migrations produce the same schema as the model. After adding a migration, launch the app
> against a copy of a real database to confirm it applies cleanly.

### Reset your local data

Close the app and delete `%LOCALAPPDATA%\SaniStock\sanistock.db` (plus any `-shm` / `-wal`
siblings). The next launch recreates and reseeds it.

### Load demo data

```bash
dotnet run --project tools/SaniStock.DemoSeeder
# or against a specific file:
dotnet run --project tools/SaniStock.DemoSeeder -- "C:\Users\<you>\AppData\Local\SaniStock\sanistock.db"
```

Writes everything **through the domain services**, so balances, ledger rows, reservations and
statuses are genuinely correct. Master data is get-or-create; the stock+orders block runs once and
is skipped if orders tagged `DEMO` already exist.

### Preview report layouts

```bash
dotnet run --project tools/SaniStock.ReportSample -- "orders-sample.pdf"
```

Renders the Orders report to PDF, one PNG per page (140 DPI), and the Orders `.xlsx`, then prints
the first 16 spreadsheet rows to the console.

> Neither tool is in `SaniStock.sln`, so `dotnet build` at the root does **not** build them. They
> also never ship with the app.

### Build for production

```bash
# 1) self-contained publish (bundles the .NET runtime + QuestPDF's Lato fonts)
dotnet publish src/SaniStock.App/SaniStock.App.csproj -c Release -r win-x64 --self-contained true -o publish

# 2) compile the installer (requires Inno Setup 6)
cd installer
iscc SaniStock.iss
# → installer/Output/SaniStock-Setup-x64.exe
```

The `.iss` script hard-fails at compile time if `..\publish\SaniStock.exe` is missing, and prints
the packaged file version via `#pragma message` so a stale publish folder is visible.

### What the installer does

- Installs to `{autopf}\SaniStock`, requires admin, x64 only, Windows 10 1607+.
- Uses Restart Manager (`CloseApplications=yes`) to close a running SaniStock rather than failing on
  locked files.
- **`PrepareToInstall` copies the existing `sanistock.db` aside** to
  `sanistock.db.before-2.2.0-<timestamp>.bak` before replacing anything, because this release's
  first launch runs a migration that rewrites existing stock rows. If the copy fails the user is asked whether to
  continue, defaulting to *No*.
- The finish page tells the user where the backup went and that all already-packed stock lands under
  a brand called "Unbranded", which they should replace with their real brands in Setup Lists.
- **The database is intentionally NOT removed on uninstall**, so a reinstall keeps existing data.

### Where data lives at runtime

| What | Location |
|------|----------|
| Database | `%LOCALAPPDATA%\SaniStock\sanistock.db` |
| Logs | `%LOCALAPPDATA%\SaniStock\logs\log-*.txt` (daily, 14 retained) |
| Restore safety copy | `sanistock.db.bak`, written next to the live DB by `BackupService.Restore` |
| Installer safety copy | `sanistock.db.before-<version>-<timestamp>.bak` |
| User backups | wherever the user picks in *Backup → Backup now* |

---

## 11. KNOWN ISSUES / TECH DEBT / TODOs

There are **no `TODO`, `FIXME`, `HACK` or `NotImplementedException` markers anywhere** in the source
— verified by search. Everything below was found by reading the code, not by reading comments.

### Correctness / robustness risks

1. **No explicit database transactions.** `BeginTransaction` and `TransactionScope` appear nowhere.
   `ProductionService.Post`, `PackingService.Post`, `AccessoryReceiptService.Post`,
   `OrderService.Book`, `DispatchService.Dispatch` and every `Reverse` call `SaveChanges()` **twice**
   — once to obtain an id, then again after posting the ledger movement. A crash or power loss
   between the two leaves a source document with no matching movement, silently understating stock
   until someone runs *Recalculate balances*. This is the highest-value fix in the codebase:
   wrap each operation in `db.Database.BeginTransaction()`.

2. ~~**Green ware and raw material are outside the ledger discipline.**~~ **Fixed.** They still
   mutate their `OnHand` directly rather than writing signed movement rows, but corrections are now
   mirrored entries rather than edits, so their entries sum to their balance and `ReconcileAll()`
   rebuilds them alongside finished goods and accessories.

3. ~~**Balances can go negative in the green/raw modules.**~~ **Fixed.** Both services now refuse an
   issue larger than the current balance, and refuse a reversal that would take it below zero.
   `AccessoryReceiptService.Reverse` gained the same guard, which it had been missing entirely.

4. **No concurrency control of any kind.** No row versions, no optimistic concurrency tokens, no
   locking, and everything runs on the UI thread. Two copies of the app pointed at the same database
   file (e.g. on a network share) would corrupt balances. There is nothing preventing that setup.

5. **`Party.Name` has no unique index**, unlike every other master-data name. Duplicate customers
   can be created silently.

5a. **Stock packed under the wrong brand is invisible to "What to Make".** Shortfall is measured on
   a combination's totals, so 100 packed for Brand B against 100 ordered for Brand A nets to zero
   and never appears — even though that order cannot ship. Deliberate: the remedy is repacking,
   which this app does not model, and recommending production would over-produce. It does mean a
   genuinely blocked order can sit unflagged. A separate "blocked by brand" list on that tab is the
   obvious fix if it bites.

5b. **There is no unpack / repack operation.** The only way to undo packing is to reverse that
   specific `PackingEntry`, which is blocked once anything has shipped for that combination and
   brand since. So a batch packed under a brand nobody is ordering is effectively stranded. This
   pre-dates brand (packing was always one-way) but brand makes it much easier to hit.

5c. **An allocation is not re-pointed when the stock under it is packed.** A line that reserved
   against the shared pool keeps a brand-less allocation; if that pool is later packed under a
   *different* brand, the line correctly refuses to steal those boxes and simply fails the physical
   check at dispatch ("Produce more first"). Known and accepted — re-pointing allocations at packing
   time is real machinery, and the failure is loud rather than silent.

6. **Restore requires a manual restart.** `BackupService.Restore` swaps the file underneath a
   running process; the app only *tells* the user to close and reopen. Any `DbContext` created
   before the swap is pointed at a replaced file. `ClearAllPools()` reduces but does not eliminate
   the risk.

### Implemented but not reachable from the UI

These all work and, where applicable, are tested — they simply have no button:

7. ~~**Order cancellation.** `OrderService.Cancel` is unreachable from the UI.~~ **Fixed** — the
   Recent Orders list on *New Order* now has a **Delete** button that calls it, shown only while
   the order can still be cancelled. The dedicated Orders screen will carry a fuller version.
8. ~~**Accessory stock PDF export.** `PdfReports.SaveAccessoryStock` has zero callers.~~
   **Fixed** — the Stock screen's Save PDF button now calls it when the Accessories tab is showing.
9. **Shortfall Excel export.** The "What to Make" tab offers PDF only.
10. ~~**Accessory receipt reversal.**~~ **Fixed** — every tab on Add Stock now has its own history
    list with an Undo action, covering accessory receipts, green ware and raw material as well as
    finished production.
11. **Audit log viewing.** Every stock action writes an `AuditLog` row, and nothing ever reads one.
12. **Green ware / raw material balances.** Movements can be posted; the resulting balances appear
    on no screen and in no report.
13. **`StockMovementType.Issue`** is declared and documented but never written by any service.
14. **`ViewModelBase.IsBusy`** is defined and never set — no screen shows a busy indicator.

### Configuration gaps

15. **`ReportService.LowStockThreshold` is hard-coded to `10`** and settable only in code. A plant
    with high-volume items has no way to tune the dashboard's low-stock count.
16. **Default admin credentials are public consts** (`admin`/`admin123`) and the app never forces a
    change. Combined with an unencrypted database, this is the weakest link in the security story.
17. **Minimum password length is 4** with no other rules.
18. **Report company name is a constant** (`"SaniStock — Sanitary Ware Works"`) baked into every PDF
    header, with no per-installation branding.

### Versioning and documentation drift

19. **[DISCREPANCY] Three different version numbers.** `SaniStock.App.csproj` says `1.0.0`; the
    installer says `2.2.0`; the About screen reads the assembly version, so it shows **1.0.0** while
    the installed product calls itself 2.2.0. The csproj `<Version>` has never been bumped for a
    release.
20. **[DISCREPANCY] `docs/README.md` references `SaniStock.slnx`**, which does not exist — the file
    is `SaniStock.sln`.
21. **Malformed installer `AppId` GUID.** `{{7B3D2C1A-9E44-4F6B-8A21-SANISTOCK002}` — the last
    segment is not hexadecimal. The `.iss` comment says this is deliberate and must never change,
    because altering it would make new versions install *alongside* the old one instead of upgrading
    it. Inno Setup treats `AppId` as an opaque string, so it works; just never "fix" it.
22. **`PROJECT_ANALYSIS_REPORT.md`** sits untracked at the repo root and may duplicate or contradict
    this document. Treat this file as the current reference.

### Testing gaps

23. **Tests mostly use `EnsureCreated()`, not `Migrate()`.** The migration chain is therefore not
    exercised by most of the suite; a migration that diverges from the model would pass those tests
    and fail on a real user's database. **Partly closed:**
    [BrandMigrationTests.cs](tests/SaniStock.Domain.Tests/BrandMigrationTests.cs) does run the real
    chain — migrating to a named earlier migration, seeding legacy rows, then migrating forward —
    and is the pattern to copy for any future data-rewriting migration. Only `AddBrand` is covered
    so far.
24. **No UI or ViewModel tests at all.** The entire `SaniStock.App` project is untested — including
    all the string→decimal parsing in the ViewModels.
25. **`SaniStock.Reports` is untested.** PDF/Excel generation is verified only by eyeballing the
    output of `tools/SaniStock.ReportSample`.
26. **No CI pipeline.** No `.github/workflows`, no automated build or test on push.

### Minor code smells

27. ~~`StockService.ReconcileAll()` assigns `var written = _db.SaveChanges();` and never uses it.~~
    **Fixed** while extending reconcile to cover green ware and raw material.
28. `MasterDataService.Upsert<T>` uses **reflection** (`typeof(T).GetProperty("Id")`) to read the
    primary key on every save. A small `IHasId` interface would be faster and compile-checked.
29. ~~**Master data can never be deleted**, only deactivated, with no delete command in the UI.~~
    **Resolved by design, and now documented.** Deactivation *is* the delete, surfaced as
    Delete/Restore — see "Nothing is ever deleted" in §1. Referential safety is the whole point.
30. `ExcelExporter.Save` exports **every public simple property** by reflection, so any property
    added to a report row record silently appears as a new spreadsheet column — and any property
    that is *not* simple is silently dropped. That is why the stock export is now hand-written:
    reflection would omit `StockRow.PackedByBrand` entirely.
31. ~~`StockViewModel`'s export buttons always export the **Finished Goods** grid, even when the
    Accessories tab is showing — a quiet surprise for the user.~~ **Fixed** — the buttons follow the
    selected tab, and the saved file is named after it. Note this is UI-layer code, so it is covered
    by no automated test (see gap 24); the accessory PDF path was verified by rendering one through
    `tools/SaniStock.ReportSample`.
32. `decimal` → `double` conversion is global. Correct for counts, but if money or unit costs are
    ever added to this model, those columns **must not** go through the same converter.

---

## 12. GLOSSARY

### Domain terms (sanitary-ware manufacturing)

| Term | Meaning |
|------|---------|
| **Sanitary ware** | Ceramic bathroom fixtures — water closets, wash basins, urinals, cisterns, bidets |
| **Green ware / green piece** | Cast but **unfired** ware. Has no grade yet (grading happens after firing), so it is keyed by item+colour only |
| **Grade** | Post-firing quality sort: **1st**, **2nd**, **3rd**. Ranked by `SortOrder`, not by name |
| **Colour** | Glaze colour (White, Ivory, Pergamon, Black, Sky Blue in the demo data) |
| **Brand** | The marque ware is packed and sold under. Decided at **packing**, not at production — the same kiln output can become any brand. Accessories have no brand |
| **Item** | A finished product model, e.g. "Wash Basin 100" |
| **Product Type** | The category an item belongs to — Water Closet, Wash Basin, Bib Cock… |
| **Accessory** | A fitting sold with or alongside ware (seat cover, pillar cock, bottle trap). Tracked **without** grade or colour, and has **no packing stage** |
| **Party** | The customer / trading party placing an order. Called "Customer" in the UI |
| **GSTIN** | Goods and Services Tax Identification Number — the Indian tax id stored on a party |
| **PCS / KG** | Default units of measure — pieces for ware and accessories, kilograms for raw material |
| **Bib cock / pillar cock / angle valve** | Types of tap/valve; seeded as product types |

### Inventory concepts as this app defines them

| Term | Meaning |
|------|---------|
| **On Hand** *(UI: "In Stock")* | Total physical stock = `RawOnHand + PackedOnHand`. **Derived, never stored** |
| **RawOnHand** *(UI: "Not Packed")* | Produced but not yet packed. Confusingly named — "raw" here means *unpacked ware*, **not raw material**. Brand-less and shared: one pool per item+grade+colour |
| **PackedOnHand** *(UI: "Packed")* | Packed and ready to ship. Dispatch draws from here first. Always belongs to exactly one brand |
| **Shared (unpacked) pool** | The `BrandId IS NULL` balance row. Any order may draw on it whatever brand it was placed for, because that ware has not been assigned a brand yet |
| **Bucket** | One of the two on-hand sub-states (`Packed` / `Raw`), plus the virtual `Shortfall` value used on allocations |
| **Reserved** *(UI: "Booked")* | Quantity held for booked orders. **Not** split by bucket — a reservation is a claim on the grade's total stock |
| **Available** *(UI: "Free")* | `OnHand − Reserved`. **Negative is normal and meaningful** — it is the shortfall signal, not an error |
| **Shortfall** | `Reserved − OnHand` where positive. Drives the "What to Make" report |
| **Driver** | An open order line explaining part of a shortfall, attributed to the grade whose stock it actually holds |
| **Low stock** | `0 < OnHand ≤ LowStockThreshold` (default 10). Distinct from shortfall |
| **Movement** | An immutable signed ledger row. The source of truth for all stock math |
| **Balance** | The cached running sum of movements. Always rebuildable via `ReconcileAll()` |
| **Reconcile** | Rebuilding every cached balance from the ledgers. Exposed as *Backup → Recalculate balances* |
| **Allocation** | An `OrderLineAllocation` row: which grade+brand+bucket a booked line drew from, at what priority |
| **Batch** *(packing)* | The set of `PackingEntry` rows posted by one multi-brand packing action, sharing a `BatchId`. Each row is still undone on its own |
| **Priority** | The draw order of an allocation. Dispatch consumes ascending; cancellation releases descending |
| **Grade chain / borrowing / fallback** | The rule that a top-grade line may be covered from the grade immediately below. One-directional, top-grade-only, never past the 2nd grade |
| **Reversal** | The only way to correct a posted entry. Writes a new linked row (`IsReversal = true`, `ReversesEntryId` set) rather than editing or deleting the original |
| **Pending** | `QuantityOrdered − QuantityDispatched` on an order or accessory line |

### Code and infrastructure terms

| Term | Meaning |
|------|---------|
| **DomainScope** | This project's unit-of-work: one `DbContext` plus all 14 domain services, created and disposed per user action |
| **`IDomainScopeFactory`** | The injected factory every ViewModel uses to open a scope |
| **`IUserContext` / `UserContext`** | Who is acting now. Used for `CreatedBy` stamping and audit rows — **not** for authorization |
| **`DomainException`** | A business-rule violation meant to be shown to the user as a friendly message, not logged as a crash |
| **`MasterList<T>`** | The reusable New/Edit/Save controller behind all eight master-data tabs |
| **`StockRow` / `ShortfallRow` / `OrderReportRow` / …** | Immutable `record` DTOs in `Domain/Models/ReportRows.cs`, returned by `ReportService` and consumed by both the UI and the report documents |
| **`ProductionInput` / `PackingInput` / `OrderInput` / `DispatchInput` / …** | Immutable `record` DTOs in `Domain/Models/Inputs.cs`, the parameter objects for every write |
| **`ApplyFinished` / `ApplyAccessory`** | The two methods on `StockService` through which *all* stock mutation flows |
| **`ORD-yyyy-NNNN` / `DN-yyyy-NNNN`** | Order and dispatch note number formats, generated per year by `NumberSequenceService` |
| **QuestPDF Community licence** | The free licence tier, activated once at startup — required or QuestPDF throws |
| **Inno Setup / `iscc`** | The installer toolchain and its command-line compiler |
| **Restart Manager** | The Windows facility Inno Setup uses to close a running SaniStock during an upgrade instead of failing on locked files |
