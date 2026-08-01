using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaniStock.Data.Migrations
{
    /// <summary>
    /// Adds Brand, the dimension that exists from packing onward.
    /// <para>
    /// Production stays brand-less: ware leaves the kiln as one undifferentiated pool and only
    /// becomes "Brand A" when someone packs it that way. So <c>StockBalances</c> splits — the row
    /// with a null <c>BrandId</c> is the shared unpacked pool for an item+grade+colour, and one row
    /// per brand carries that brand's packed stock. Orders are placed against a brand, and each
    /// allocation records which physical source it drew from (brand + grade, or brand-less for the
    /// shared pool).
    /// </para>
    /// <para>
    /// <b>What happens to existing data.</b> Pre-existing packed stock has no brand recorded
    /// anywhere, so it is assigned to a seeded <c>UNBRANDED</c> brand — left <em>active</em>, so the
    /// stock stays visible and shippable, and an admin can deactivate it once it has drained.
    /// <c>DbSeeder</c> seeds the same code on fresh databases, so an upgraded install and a new one
    /// converge on one identical brand rather than two near-identical ones. Every open order line
    /// and every packing entry is assigned to it too.
    /// </para>
    /// <para>
    /// <b>Reserved deliberately stays where it is.</b> Every pre-existing reservation remains on the
    /// brand-less row and every back-filled allocation keeps a null <c>BrandId</c>, rather than
    /// being split packed-first the way <c>BucketFree</c> used to deem it. That split cannot be
    /// expressed in the ledger — a reservation movement carries no bucket — so the next
    /// <c>ReconcileAll()</c> would silently undo it and the cache would disagree with the ledger.
    /// Leaving it brand-less is self-consistent, survives reconcile, leaves each combination's total
    /// Available unchanged, and still ships correctly: the old lines are Unbranded, the old packed
    /// stock is Unbranded, and a brand-less allocation falls back to its line's brand's packed row.
    /// The only visible effect is that a brand-less row may read negative on its own immediately
    /// after upgrade, which is why shortfall is measured across the whole combination.
    /// </para>
    /// </summary>
    public partial class AddBrand : Migration
    {
        /// <summary>Resolves the seeded brand id inline, so the SQL never hard-codes a primary key.</summary>
        private const string UnbrandedId = "(SELECT Id FROM Brands WHERE Code = 'UNBRANDED')";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- 1. The brand table, and the one brand all existing data belongs to -------------

            migrationBuilder.CreateTable(
                name: "Brands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brands", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Brands_Code",
                table: "Brands",
                column: "Code",
                unique: true);

            migrationBuilder.Sql(
                "INSERT INTO Brands (Code, Name, IsActive) VALUES ('UNBRANDED', 'Unbranded', 1);");

            // ---- 2. New columns ------------------------------------------------------------------
            // Brand is nullable wherever brand-less is a real state (the shared unpacked pool, a
            // reservation drawn from it) and required where it is not (a packing entry, an order).

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ItemId_GradeId_ColourId_Date",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockBalances_ItemId_GradeId_ColourId",
                table: "StockBalances");

            migrationBuilder.AddColumn<int>(
                name: "BrandId",
                table: "StockMovements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrandId",
                table: "StockBalances",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BatchId",
                table: "PackingEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrandId",
                table: "PackingEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BrandId",
                table: "OrderLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BrandId",
                table: "OrderLineAllocations",
                type: "INTEGER",
                nullable: true);

            // ---- 3. Back-fill, before any foreign key can reject a placeholder 0 ------------------

            // Existing packing entries and order lines predate brand entirely.
            migrationBuilder.Sql($"UPDATE PackingEntries SET BrandId = {UnbrandedId};");
            migrationBuilder.Sql($"UPDATE OrderLines SET BrandId = {UnbrandedId};");

            // BatchId is left null on historic rows; a null batch reads as a batch of one
            // (`BatchId ?? Id`), which is exactly what every pre-existing packing was.

            // OrderLineAllocations.BrandId stays null everywhere, including rows whose Bucket was
            // Packed. See the class remarks: pairing a packed allocation with the Unbranded brand
            // would contradict where this migration leaves Reserved, and dispatch would then look
            // for the reservation on a row that is not holding it.

            // Split each balance: the original row keeps the unpacked pool and drops its packed
            // quantity; a new Unbranded row picks that packed quantity up. Reserved stays put.
            // The insert reads rows with a null BrandId and writes rows with one, so it cannot
            // cascade into its own output.
            migrationBuilder.Sql($@"
                INSERT INTO StockBalances (ItemId, GradeId, ColourId, BrandId, RawOnHand, PackedOnHand, Reserved)
                SELECT ItemId, GradeId, ColourId, {UnbrandedId}, 0, PackedOnHand, 0
                FROM StockBalances
                WHERE BrandId IS NULL AND PackedOnHand <> 0;");

            migrationBuilder.Sql(
                "UPDATE StockBalances SET PackedOnHand = 0 WHERE BrandId IS NULL;");

            // Split the ledger the same way, so ReconcileAll() reproduces the balances above rather
            // than collapsing the brands back together. A movement belongs to exactly one balance
            // row, so any historic row carrying both legs (a packing: -raw/+packed) becomes two:
            // the raw leg stays brand-less, the packed leg moves to Unbranded.
            migrationBuilder.Sql($@"
                INSERT INTO StockMovements
                    (Date, ItemId, GradeId, ColourId, BrandId, Type,
                     DeltaRawOnHand, DeltaPackedOnHand, DeltaReserved,
                     SourceType, SourceId, CreatedBy, CreatedAt, Remarks)
                SELECT Date, ItemId, GradeId, ColourId, {UnbrandedId}, Type,
                       0, DeltaPackedOnHand, 0,
                       SourceType, SourceId, CreatedBy, CreatedAt, Remarks
                FROM StockMovements
                WHERE BrandId IS NULL AND DeltaPackedOnHand <> 0;");

            migrationBuilder.Sql(
                "UPDATE StockMovements SET DeltaPackedOnHand = 0 WHERE BrandId IS NULL AND DeltaPackedOnHand <> 0;");

            // ---- 4. Indexes ------------------------------------------------------------------------

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_BrandId",
                table: "StockMovements",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ItemId_GradeId_ColourId_BrandId_Date",
                table: "StockMovements",
                columns: new[] { "ItemId", "GradeId", "ColourId", "BrandId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_StockBalances_BrandId",
                table: "StockBalances",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_StockBalances_ItemId_GradeId_ColourId_BrandId",
                table: "StockBalances",
                columns: new[] { "ItemId", "GradeId", "ColourId", "BrandId" },
                unique: true);

            // SQLite treats NULLs as distinct in a unique index, so the four-column index above
            // would allow several brand-less rows for one combination. This closes that hole.
            migrationBuilder.CreateIndex(
                name: "IX_StockBalances_Item_Grade_Colour_Unbranded",
                table: "StockBalances",
                columns: new[] { "ItemId", "GradeId", "ColourId" },
                unique: true,
                filter: "\"BrandId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PackingEntries_BatchId",
                table: "PackingEntries",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PackingEntries_BrandId",
                table: "PackingEntries",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_BrandId",
                table: "OrderLines",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLineAllocations_BrandId",
                table: "OrderLineAllocations",
                column: "BrandId");

            // ---- 5. Foreign keys -------------------------------------------------------------------
            // Restrict throughout: brands are deactivate-only master data and every reference is
            // history. Adding these rebuilds each table in SQLite, which copies the back-filled rows
            // above across intact.

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLineAllocations_Brands_BrandId",
                table: "OrderLineAllocations",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLines_Brands_BrandId",
                table: "OrderLines",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PackingEntries_Brands_BrandId",
                table: "PackingEntries",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockBalances_Brands_BrandId",
                table: "StockBalances",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Brands_BrandId",
                table: "StockMovements",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fold the per-brand packed rows back into the single row per item+grade+colour, and
            // the split ledger legs back into one movement, before the columns that distinguish
            // them disappear. Which brand the stock was packed under is lost — that information
            // has nowhere to live in the old schema.
            migrationBuilder.Sql(@"
                UPDATE StockBalances SET PackedOnHand = COALESCE((
                    SELECT SUM(b.PackedOnHand) FROM StockBalances b
                    WHERE b.ItemId = StockBalances.ItemId AND b.GradeId = StockBalances.GradeId
                      AND b.ColourId = StockBalances.ColourId AND b.BrandId IS NOT NULL), 0)
                WHERE BrandId IS NULL;");

            migrationBuilder.Sql("DELETE FROM StockBalances WHERE BrandId IS NOT NULL;");
            migrationBuilder.Sql("DELETE FROM StockMovements WHERE BrandId IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderLineAllocations_Brands_BrandId",
                table: "OrderLineAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderLines_Brands_BrandId",
                table: "OrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PackingEntries_Brands_BrandId",
                table: "PackingEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_StockBalances_Brands_BrandId",
                table: "StockBalances");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Brands_BrandId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "Brands");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_BrandId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ItemId_GradeId_ColourId_BrandId_Date",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockBalances_BrandId",
                table: "StockBalances");

            migrationBuilder.DropIndex(
                name: "IX_StockBalances_Item_Grade_Colour_Unbranded",
                table: "StockBalances");

            migrationBuilder.DropIndex(
                name: "IX_StockBalances_ItemId_GradeId_ColourId_BrandId",
                table: "StockBalances");

            migrationBuilder.DropIndex(
                name: "IX_PackingEntries_BatchId",
                table: "PackingEntries");

            migrationBuilder.DropIndex(
                name: "IX_PackingEntries_BrandId",
                table: "PackingEntries");

            migrationBuilder.DropIndex(
                name: "IX_OrderLines_BrandId",
                table: "OrderLines");

            migrationBuilder.DropIndex(
                name: "IX_OrderLineAllocations_BrandId",
                table: "OrderLineAllocations");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "StockBalances");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "PackingEntries");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "PackingEntries");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "OrderLineAllocations");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ItemId_GradeId_ColourId_Date",
                table: "StockMovements",
                columns: new[] { "ItemId", "GradeId", "ColourId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_StockBalances_ItemId_GradeId_ColourId",
                table: "StockBalances",
                columns: new[] { "ItemId", "GradeId", "ColourId" },
                unique: true);
        }
    }
}
