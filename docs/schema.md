# SaniStock — data model

## Master data
- **Item** (code, name, unit, active) — finished ware
- **Grade** (name, sort, active) — 1st/2nd/3rd, seeded, extensible
- **Colour** (name, hex?, active)
- **Accessory** (code, name, unit, active)
- **Party** (name, address?, contact?, gstin?, active) — customer
- **RawMaterial** (name, unit, active)

## Ledgers (immutable, append-only) and cached balances

| Stock type | Ledger (source of truth) | Cached balance | Key |
|------------|--------------------------|----------------|-----|
| Finished goods | `StockMovement` (signed ΔOnHand, ΔReserved) | `StockBalance` (OnHand, Reserved, Available=OnHand−Reserved) | Item + Grade + Colour |
| Accessories | `AccessoryStockMovement` | `AccessoryStockBalance` | Accessory |
| Green (unfired) ware | `GreenPieceEntry` (in/out) | `GreenPieceBalance` (OnHand) | Item + Colour |
| Raw material | `RawMaterialEntry` (in/out) | `RawMaterialBalance` (OnHand) | RawMaterial |

`StockBalance`/`AccessoryStockBalance` are always rebuildable as the sum of their movement rows
(`StockService.ReconcileAll`).

## Source documents
- **ProductionEntry** (item+grade+colour, qty, immutable; `IsReversal`/`ReversesEntryId` for corrections)
  → `StockMovement` `Production` (+OnHand)
- **AccessoryReceipt** → `AccessoryStockMovement` `Production` (+OnHand)
- **Order** (OrderNo, party, date, status) with **OrderLine** (item+grade+colour, ordered/dispatched/reserved)
  and **OrderAccessoryLine**
  - Booking → movement `Reservation` (+Reserved)
  - Cancel → movement `ReservationRelease` (−Reserved) for undispatched qty
- **DispatchEntry** (DispatchNo, order, date) with **DispatchLine** / **DispatchAccessoryLine**
  → movement `Dispatch` (−OnHand, −Reserved); advances OrderLine + Order status

## Movement types (signed)
`Production` (+OnHand) · `Reservation` (+Reserved) · `ReservationRelease` (−Reserved) ·
`Dispatch` (−OnHand, −Reserved) · `Adjustment` (reversal) · `Issue` (−OnHand, green/raw out)

## System
- **User** (username, BCrypt hash, role Admin/Operator, active)
- **AuditLog** (timestamp, username, action, entity, before/after qty)

## Order status
`Booked` → `PartiallyDispatched` → `Dispatched`; or `Cancelled`. Computed from line pending quantities
after each dispatch.
