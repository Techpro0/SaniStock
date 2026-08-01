# SaniStock — data model

## Master data
- **ProductType** (code, name, active) — ware category (Water Closet, Wash Basin, Bib Cock…), seeded, extensible. Sits above Item.
- **Item** (code, name, unit, active, productType?) — finished ware; optional `ProductTypeId` (SetNull on delete, nullable so legacy items are unaffected)
- **Grade** (name, sort, active) — 1st/2nd/3rd, seeded, extensible
- **Colour** (name, hex?, active)
- **Brand** (code, name, active) — the marque ware is packed and sold under. Assigned at **packing**, not production; seeded with one `UNBRANDED` row so packing works on a fresh database
- **Accessory** (code, name, unit, active)
- **ItemAccessoryDefault** (item, accessory, qtyPerUnit, active) — the bundling "recipe": accessories booked automatically per item unit ordered. Unique per item+accessory.
- **Party** (name, address?, contact?, gstin?, active) — customer
- **RawMaterial** (name, unit, active)

## Ledgers (immutable, append-only) and cached balances

| Stock type | Ledger (source of truth) | Cached balance | Key |
|------------|--------------------------|----------------|-----|
| Finished goods | `StockMovement` (signed ΔRawOnHand, ΔPackedOnHand, ΔReserved) | `StockBalance` (RawOnHand, PackedOnHand, Reserved) | Item + Grade + Colour + Brand? |
| Accessories | `AccessoryStockMovement` | `AccessoryStockBalance` | Accessory |
| Green (unfired) ware | `GreenPieceEntry` (in/out) | `GreenPieceBalance` (OnHand) | Item + Colour |
| Raw material | `RawMaterialEntry` (in/out) | `RawMaterialBalance` (OnHand) | RawMaterial |

`StockBalance`/`AccessoryStockBalance` are always rebuildable as the sum of their movement rows
(`StockService.ReconcileAll`). Each finished bucket rebuilds from its own signed delta, so no
knowledge of packing or allocation order is needed to reconcile.

### The two finished on-hand buckets, and how brand splits them

Finished on-hand is split into **RawOnHand** (produced, not yet packed) and **PackedOnHand** (ready to
ship). `OnHand` and `DeltaOnHand` are *derived* (`Raw + Packed`), not stored columns, so the split and
the total cannot drift apart. `Available` is still `OnHand − Reserved`.

**Brand splits the balance row.** One item+grade+colour spans:

- the row with `BrandId IS NULL` — the **shared unpacked pool**; `PackedOnHand` is always 0 here
- one row per brand — that brand's **packed** stock; `RawOnHand` is always 0 there

That invariant is enforced in `StockService.ApplyFinished`, not by a check constraint: balances are a
cache that `ReconcileAll()` rewrites wholesale, and a constraint would make the repair crash on the
very anomaly it exists to expose. Two unique indexes keep one row per key —
`(Item, Grade, Colour, BrandId)` plus a **filtered** one on `(Item, Grade, Colour) WHERE BrandId IS
NULL`, because SQLite treats NULLs as distinct.

A movement belongs to exactly one row, so packing writes **two**: a `−Raw` leg on the brand-less key
and a `+Packed` leg on the brand's key. That is what lets `ReconcileAll` rebuild the per-brand split
from the ledger alone.

`Reserved` is recorded **on the row it was drawn from** — the shared pool or one brand's packed stock —
so unreserved quantity is a plain subtraction and no bucket-order heuristic is needed. Do **not** show
a single row's `Available` to a user, though: dispatch may take a reservation recorded on one row out
of its sibling, so a row can read negative while the combination as a whole is covered. Availability
and shortfall are judged across all of a combination's rows.

## Source documents
- **ProductionEntry** (item+grade+colour, qty, immutable; `IsReversal`/`ReversesEntryId` for corrections)
  → `StockMovement` `Production` (**+RawOnHand** — newly made ware is never packed until a packing entry says so).
  Reversal is blocked once the ware has been packed: undo the packing first.
- **PackingEntry** (item+grade+colour+**brand**, qty, `BatchId?`, immutable; `IsReversal`/`ReversesEntryId`
  for corrections) → two `Packing` movements (−RawOnHand on the brand-less key, +PackedOnHand on the
  brand's key), netting to zero on the total. Packing is what **assigns brand**.
  One action may be split across several brands, posting **one row per brand** under a shared `BatchId`
  rather than a header with child lines — which keeps reversal exactly as it was, so undoing one brand's
  portion never forces undoing the others. The brand lines are validated **together** against what is
  unpacked (all-or-nothing). Reversal is blocked once anything has shipped for that combination **and
  brand** since the packing was posted (measured by ledger position, not wall-clock time; the *earliest*
  movement for the entry is used, since a packing now has two legs).
- **AccessoryReceipt** → `AccessoryStockMovement` `Production` (+OnHand). Accessories have no packing stage,
  and no brand — including ones auto-bundled under a branded item line.
- **Order** (OrderNo, party, date, status) with **OrderLine** (item+grade+colour+**brand**, ordered/dispatched/reserved)
  and **OrderAccessoryLine** (accessory, ordered/dispatched/reserved, `SourceOrderLineId?` — set when the line
  was auto-attached from an item's `ItemAccessoryDefault`; null for a manually-added standalone accessory)
  - Booking → movement `Reservation` (+Reserved), one per **source row** (grade + brand) the line drew from, plus
    **OrderLineAllocation** rows recording the split (see below). Each item line also auto-reserves its
    active `ItemAccessoryDefault` accessories (qtyPerUnit × ordered), except any excluded per-line at
    booking; accessories are unaffected by grade allocation.
  - Cancel → movement `ReservationRelease` (−Reserved) for undispatched qty, released against the exact
    recorded sources in reverse draw order, on item **and** bundled accessory lines
- **OrderLineAllocation** (orderLine, grade, **brand?**, bucket, priority, qty, dispatched, released) — which
  physical stock covers a line. `GradeId` + `BrandId` are authoritative: together they identify the balance
  row whose `Reserved` was raised, so they are what dispatch deducts and cancellation releases. `BrandId` is
  **null** for a draw from the shared unpacked pool and for a shortfall. `Bucket` is the booking-time
  snapshot and is advisory, because packing may move the quantity before it ships. Not immutable — it
  tracks how much has since shipped or been released, exactly as `OrderLine` does for the line as a whole.
- **DispatchEntry** (DispatchNo, order, date) with **DispatchLine** / **DispatchAccessoryLine**
  → movement `Dispatch` (−OnHand, −Reserved), taking **packed stock first and falling back to unpacked**;
  advances OrderLine + Order status.
  Brand adds no new rule here, but it does split the bookkeeping: the reservation is released from the row
  it was recorded against, while the goods come out of whichever of the two rows for that
  item+grade+colour+**line brand** actually holds them. So one draw can write two `Dispatch` movements.
  That sibling fallback is what keeps stock booked while unpacked shippable once it is packed under the
  order's brand, and vice versa. **Another brand's packed stock is never touched.**
  Each brand's packed row belongs to one dispatch key, but the unpacked pool is shared between keys
  that differ only in brand, so it is rationed as they are planned — otherwise two brands on one
  order would each validate against the whole pool and together ship more than exists.

## Grade-priority allocation (1st grade only)

A **top-grade** line (lowest `Grade.SortOrder` among active grades) is covered by walking four sources
in order, moving on only when one is exhausted:

1. packed 1st grade *(this line's brand)* → 2. unpacked 1st grade *(shared)* →
3. packed 2nd grade *(this line's brand)* → 4. unpacked 2nd grade *(shared)*

**Brand narrows the packed steps only.** Another brand's packed pieces are in the wrong boxes and can
never cover this order. The unpacked steps stay brand-less: ware in the pool has not been assigned a
brand yet, so any order may draw on it whatever brand it was placed for, and whoever packs it decides.

Whatever the four cannot cover is recorded as a `Shortfall` allocation against the **ordered** grade, which
is what drives that grade's `Available` negative and puts it on the planning report. The fallback is
one-directional and top-grade-only: 2nd- and 3rd-grade lines reserve solely against their own grade, and
borrowing never reaches past the 2nd grade. Because each grade's `Reserved` only ever counts what was drawn
from that grade, `Available` per grade already nets out cross-grade reservations — stock is never
double-counted. The chain is derived from `SortOrder`, not from grade names or ids, since grades are
user-maintained master data.

## Movement types (signed)
`Production` (+RawOnHand) · `Packing` (−RawOnHand, +PackedOnHand) · `Reservation` (+Reserved) ·
`ReservationRelease` (−Reserved) · `Dispatch` (−OnHand packed-first, −Reserved) ·
`Adjustment` (reversal) · `Issue` (−OnHand, green/raw out)

## System
- **User** (username, BCrypt hash, role Admin/Operator, active)
- **AuditLog** (timestamp, username, action, entity, before/after qty)

## Order status
`Booked` → `PartiallyDispatched` → `Dispatched`; or `Cancelled`. Computed from line pending quantities
after each dispatch.
