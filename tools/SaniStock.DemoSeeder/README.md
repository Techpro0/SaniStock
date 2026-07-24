# SaniStock.DemoSeeder

A small dev/demo tool that loads a realistic sanitary-ware dataset into a SaniStock
database, for showing the app to clients. It writes everything **through the domain
services**, so stock balances, ledger movements, reservations and order statuses are
all genuinely correct (not hand-faked).

## What it creates

- **Colours**: White, Ivory, Pergamon, Black, Sky Blue
- **Items** (each tagged with a product type): One/Two Piece Closet, Wall Hung EWC,
  Counter/Pedestal/Table Top Basin, Wall Mounted Urinal, Dual Flush Cistern
- **Accessories**: Seat Cover, Flush Tank Fitting, Connection Pipe, Fixing Bolt Set,
  Pillar Cock, Waste Coupling, Bottle Trap
- **Bundling recipes** (`ItemAccessoryDefault`) — e.g. One Piece Closet → Seat Cover +
  Flush Fitting + Fixing Bolt
- **Customers**: five Gujarat/Maharashtra trading parties
- **Raw materials** + a couple of green-ware movements
- Finished-goods **production** and **accessory receipts** (opening stock)
- **Orders** covering every status:
  - Booked (bundled accessories auto-reserved)
  - Partially Dispatched
  - Fully Dispatched
  - Booked with a per-line accessory **excluded**
  - Cancelled (reservation released)
  - A **shortfall** order (booked > produced → negative Available)

## Run it

From the repo root, targeting the app's real database (default):

```bash
dotnet run --project tools/SaniStock.DemoSeeder
```

Or point it at a specific database file:

```bash
dotnet run --project tools/SaniStock.DemoSeeder -- "C:\Users\<you>\AppData\Local\SaniStock\sanistock.db"
```

## Safe to re-run

- Master data is **get-or-create** — existing colours/items/accessories/customers are reused.
- The stock + orders block runs **once**; it is skipped if demo orders (remarks tagged
  `DEMO`) already exist. Delete those orders, or use a fresh database, to reseed.

> Not part of `SaniStock.slnx` — it's a standalone tool, so it never ships with the app.
