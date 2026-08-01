using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaniStock.Data.Migrations
{
    /// <summary>
    /// Adds the packing stage and grade-priority order allocation.
    /// <para>
    /// Existing stock is migrated into the <em>unpacked</em> bucket: the renames below carry every
    /// StockBalance.OnHand and StockMovement.DeltaOnHand value straight into its RawOnHand /
    /// DeltaRawOnHand counterpart, so the ledger and the cached balances agree and a
    /// <c>ReconcileAll</c> immediately after this migration reproduces exactly the same numbers.
    /// Nothing is frozen by that choice, because dispatch falls back to unpacked stock when packed
    /// stock is short — the packing screen is how stock moves into the packed bucket from here on.
    /// </para>
    /// </summary>
    public partial class AddPackingAndGradeAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DeltaOnHand",
                table: "StockMovements",
                newName: "DeltaRawOnHand");

            migrationBuilder.RenameColumn(
                name: "OnHand",
                table: "StockBalances",
                newName: "RawOnHand");

            migrationBuilder.AddColumn<double>(
                name: "DeltaPackedOnHand",
                table: "StockMovements",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PackedOnHand",
                table: "StockBalances",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "OrderLineAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderLineId = table.Column<int>(type: "INTEGER", nullable: false),
                    GradeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Bucket = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<double>(type: "REAL", nullable: false),
                    QuantityDispatched = table.Column<double>(type: "REAL", nullable: false),
                    QuantityReleased = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLineAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderLineAllocations_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLineAllocations_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackingEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    GradeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ColourId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<double>(type: "REAL", nullable: false),
                    Remarks = table.Column<string>(type: "TEXT", nullable: true),
                    IsReversal = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReversesEntryId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackingEntries_Colours_ColourId",
                        column: x => x.ColourId,
                        principalTable: "Colours",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackingEntries_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackingEntries_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLineAllocations_GradeId",
                table: "OrderLineAllocations",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLineAllocations_OrderLineId_Priority",
                table: "OrderLineAllocations",
                columns: new[] { "OrderLineId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_PackingEntries_ColourId",
                table: "PackingEntries",
                column: "ColourId");

            migrationBuilder.CreateIndex(
                name: "IX_PackingEntries_GradeId",
                table: "PackingEntries",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_PackingEntries_ItemId_GradeId_ColourId_Date",
                table: "PackingEntries",
                columns: new[] { "ItemId", "GradeId", "ColourId", "Date" });

            // Give every still-open order line the allocation row it would have been booked with,
            // so dispatch and cancellation can unwind it through the same path as new orders:
            // its own grade (orders booked before this never borrowed across grades) and the
            // unpacked bucket (which is where all pre-existing stock now sits). Quantity is what
            // is still held, not what was originally ordered — already-shipped quantity needs no
            // allocation. Status 0 = Booked, 1 = PartiallyDispatched; Bucket 1 = Raw.
            migrationBuilder.Sql(@"
                INSERT INTO OrderLineAllocations
                    (OrderLineId, GradeId, Bucket, Priority, Quantity, QuantityDispatched, QuantityReleased)
                SELECT l.Id, l.GradeId, 1, 0, l.QuantityReserved, 0, 0
                FROM OrderLines l
                JOIN Orders o ON o.Id = l.OrderId
                WHERE o.Status IN (0, 1) AND l.QuantityReserved > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderLineAllocations");

            migrationBuilder.DropTable(
                name: "PackingEntries");

            migrationBuilder.DropColumn(
                name: "DeltaPackedOnHand",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "PackedOnHand",
                table: "StockBalances");

            migrationBuilder.RenameColumn(
                name: "DeltaRawOnHand",
                table: "StockMovements",
                newName: "DeltaOnHand");

            migrationBuilder.RenameColumn(
                name: "RawOnHand",
                table: "StockBalances",
                newName: "OnHand");
        }
    }
}
