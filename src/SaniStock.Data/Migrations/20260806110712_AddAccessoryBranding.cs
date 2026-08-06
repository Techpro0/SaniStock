using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaniStock.Data.Migrations
{
    /// <summary>
    /// Gives accessories the same brand split finished ware already has: a shared unpacked pool plus
    /// one packed row per brand, an <see cref="Entities.AccessoryPackingEntry"/> packing stage, and
    /// <see cref="Entities.OrderAccessoryLineAllocation"/> recording which source an accessory line's
    /// reservation actually drew from.
    /// <para>
    /// <b>What happens to existing data.</b> Accessories never had a packing stage before this
    /// migration, so every existing accessory balance and movement is already entirely "unpacked" —
    /// unlike <c>AddBrand</c>, there is no packed quantity to split out. The column renames below
    /// (<c>OnHand</c>→<c>RawOnHand</c>, <c>DeltaOnHand</c>→<c>DeltaRawOnHand</c>) carry every existing
    /// value straight into the unpacked bucket for free; <c>PackedOnHand</c>/<c>DeltaPackedOnHand</c>
    /// default to 0 and <c>BrandId</c> stays null, which is exactly the pre-migration state.
    /// </para>
    /// <para>
    /// <see cref="Entities.OrderAccessoryLine.BrandId"/> is the one thing that needs a real backfill,
    /// since every existing row predates the column: an auto-bundled line (<c>SourceOrderLineId</c>
    /// set) takes its parent item line's brand — the historically correct answer, since that is the
    /// brand the recipe was attached under — and a standalone line falls back to the seeded
    /// <c>Unbranded</c> brand, exactly as <c>AddBrand</c> did for <c>OrderLines</c>.
    /// </para>
    /// <para>
    /// Every still-open accessory line also gets the <c>OrderAccessoryLineAllocation</c> row it would
    /// have been booked with — brand-less, <c>Bucket = Raw</c>, <c>Priority = 0</c> — mirroring
    /// <c>AddPackingAndGradeAllocation</c>'s backfill for <c>OrderLineAllocations</c>.
    /// </para>
    /// </summary>
    public partial class AddAccessoryBranding : Migration
    {
        /// <summary>Resolves the seeded brand id inline, so the SQL never hard-codes a primary key.</summary>
        private const string UnbrandedId = "(SELECT Id FROM Brands WHERE Code = 'UNBRANDED')";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccessoryStockMovements_AccessoryId_Date",
                table: "AccessoryStockMovements");

            migrationBuilder.DropIndex(
                name: "IX_AccessoryStockBalances_AccessoryId",
                table: "AccessoryStockBalances");

            migrationBuilder.RenameColumn(
                name: "DeltaOnHand",
                table: "AccessoryStockMovements",
                newName: "DeltaRawOnHand");

            migrationBuilder.RenameColumn(
                name: "OnHand",
                table: "AccessoryStockBalances",
                newName: "RawOnHand");

            migrationBuilder.AddColumn<int>(
                name: "BrandId",
                table: "OrderAccessoryLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BrandId",
                table: "AccessoryStockMovements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DeltaPackedOnHand",
                table: "AccessoryStockMovements",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "BrandId",
                table: "AccessoryStockBalances",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PackedOnHand",
                table: "AccessoryStockBalances",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "AccessoryPackingEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AccessoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    BrandId = table.Column<int>(type: "INTEGER", nullable: false),
                    BatchId = table.Column<int>(type: "INTEGER", nullable: true),
                    Quantity = table.Column<double>(type: "REAL", nullable: false),
                    Remarks = table.Column<string>(type: "TEXT", nullable: true),
                    IsReversal = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReversesEntryId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessoryPackingEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessoryPackingEntries_Accessories_AccessoryId",
                        column: x => x.AccessoryId,
                        principalTable: "Accessories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessoryPackingEntries_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderAccessoryLineAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderAccessoryLineId = table.Column<int>(type: "INTEGER", nullable: false),
                    BrandId = table.Column<int>(type: "INTEGER", nullable: true),
                    Bucket = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<double>(type: "REAL", nullable: false),
                    QuantityDispatched = table.Column<double>(type: "REAL", nullable: false),
                    QuantityReleased = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderAccessoryLineAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderAccessoryLineAllocations_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderAccessoryLineAllocations_OrderAccessoryLines_OrderAccessoryLineId",
                        column: x => x.OrderAccessoryLineId,
                        principalTable: "OrderAccessoryLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ---- Back-fill, before the foreign key below can reject the placeholder 0 -------------

            // Auto-bundled lines take their parent item line's brand: the recipe was attached to that
            // line, so its accessory should prefer the same brand's packed stock, exactly as booking
            // now does for every new order.
            migrationBuilder.Sql(@"
                UPDATE OrderAccessoryLines
                SET BrandId = (
                    SELECT ol.BrandId FROM OrderLines ol WHERE ol.Id = OrderAccessoryLines.SourceOrderLineId)
                WHERE SourceOrderLineId IS NOT NULL;");

            // Standalone lines predate brand entirely, same treatment AddBrand gave OrderLines.
            migrationBuilder.Sql($@"
                UPDATE OrderAccessoryLines
                SET BrandId = {UnbrandedId}
                WHERE SourceOrderLineId IS NULL;");

            // Every still-open accessory line gets the allocation row it would have been booked with:
            // brand-less, drawn from the (only) unpacked bucket, first and only priority.
            migrationBuilder.Sql(@"
                INSERT INTO OrderAccessoryLineAllocations
                    (OrderAccessoryLineId, BrandId, Bucket, Priority, Quantity, QuantityDispatched, QuantityReleased)
                SELECT Id, NULL, 1, 0, QuantityReserved, 0, 0
                FROM OrderAccessoryLines
                WHERE QuantityReserved > 0;");

            migrationBuilder.CreateIndex(
                name: "IX_OrderAccessoryLines_BrandId",
                table: "OrderAccessoryLines",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessoryStockMovements_AccessoryId_BrandId_Date",
                table: "AccessoryStockMovements",
                columns: new[] { "AccessoryId", "BrandId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessoryStockMovements_BrandId",
                table: "AccessoryStockMovements",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessoryStockBalances_Accessory_Unbranded",
                table: "AccessoryStockBalances",
                column: "AccessoryId",
                unique: true,
                filter: "\"BrandId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccessoryStockBalances_AccessoryId_BrandId",
                table: "AccessoryStockBalances",
                columns: new[] { "AccessoryId", "BrandId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessoryStockBalances_BrandId",
                table: "AccessoryStockBalances",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessoryPackingEntries_AccessoryId_Date",
                table: "AccessoryPackingEntries",
                columns: new[] { "AccessoryId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessoryPackingEntries_BatchId",
                table: "AccessoryPackingEntries",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessoryPackingEntries_BrandId",
                table: "AccessoryPackingEntries",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderAccessoryLineAllocations_BrandId",
                table: "OrderAccessoryLineAllocations",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderAccessoryLineAllocations_OrderAccessoryLineId_Priority",
                table: "OrderAccessoryLineAllocations",
                columns: new[] { "OrderAccessoryLineId", "Priority" });

            migrationBuilder.AddForeignKey(
                name: "FK_AccessoryStockBalances_Brands_BrandId",
                table: "AccessoryStockBalances",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AccessoryStockMovements_Brands_BrandId",
                table: "AccessoryStockMovements",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderAccessoryLines_Brands_BrandId",
                table: "OrderAccessoryLines",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessoryStockBalances_Brands_BrandId",
                table: "AccessoryStockBalances");

            migrationBuilder.DropForeignKey(
                name: "FK_AccessoryStockMovements_Brands_BrandId",
                table: "AccessoryStockMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderAccessoryLines_Brands_BrandId",
                table: "OrderAccessoryLines");

            migrationBuilder.DropTable(
                name: "AccessoryPackingEntries");

            migrationBuilder.DropTable(
                name: "OrderAccessoryLineAllocations");

            migrationBuilder.DropIndex(
                name: "IX_OrderAccessoryLines_BrandId",
                table: "OrderAccessoryLines");

            migrationBuilder.DropIndex(
                name: "IX_AccessoryStockMovements_AccessoryId_BrandId_Date",
                table: "AccessoryStockMovements");

            migrationBuilder.DropIndex(
                name: "IX_AccessoryStockMovements_BrandId",
                table: "AccessoryStockMovements");

            migrationBuilder.DropIndex(
                name: "IX_AccessoryStockBalances_Accessory_Unbranded",
                table: "AccessoryStockBalances");

            migrationBuilder.DropIndex(
                name: "IX_AccessoryStockBalances_AccessoryId_BrandId",
                table: "AccessoryStockBalances");

            migrationBuilder.DropIndex(
                name: "IX_AccessoryStockBalances_BrandId",
                table: "AccessoryStockBalances");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "OrderAccessoryLines");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "AccessoryStockMovements");

            migrationBuilder.DropColumn(
                name: "DeltaPackedOnHand",
                table: "AccessoryStockMovements");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "AccessoryStockBalances");

            migrationBuilder.DropColumn(
                name: "PackedOnHand",
                table: "AccessoryStockBalances");

            migrationBuilder.RenameColumn(
                name: "DeltaRawOnHand",
                table: "AccessoryStockMovements",
                newName: "DeltaOnHand");

            migrationBuilder.RenameColumn(
                name: "RawOnHand",
                table: "AccessoryStockBalances",
                newName: "OnHand");

            migrationBuilder.CreateIndex(
                name: "IX_AccessoryStockMovements_AccessoryId_Date",
                table: "AccessoryStockMovements",
                columns: new[] { "AccessoryId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessoryStockBalances_AccessoryId",
                table: "AccessoryStockBalances",
                column: "AccessoryId",
                unique: true);
        }
    }
}
