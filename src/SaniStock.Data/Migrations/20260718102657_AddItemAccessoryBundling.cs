using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaniStock.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddItemAccessoryBundling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceOrderLineId",
                table: "OrderAccessoryLines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ItemAccessoryDefaults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    AccessoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    QtyPerUnit = table.Column<double>(type: "REAL", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemAccessoryDefaults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemAccessoryDefaults_Accessories_AccessoryId",
                        column: x => x.AccessoryId,
                        principalTable: "Accessories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemAccessoryDefaults_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderAccessoryLines_SourceOrderLineId",
                table: "OrderAccessoryLines",
                column: "SourceOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemAccessoryDefaults_AccessoryId",
                table: "ItemAccessoryDefaults",
                column: "AccessoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemAccessoryDefaults_ItemId_AccessoryId",
                table: "ItemAccessoryDefaults",
                columns: new[] { "ItemId", "AccessoryId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderAccessoryLines_OrderLines_SourceOrderLineId",
                table: "OrderAccessoryLines",
                column: "SourceOrderLineId",
                principalTable: "OrderLines",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderAccessoryLines_OrderLines_SourceOrderLineId",
                table: "OrderAccessoryLines");

            migrationBuilder.DropTable(
                name: "ItemAccessoryDefaults");

            migrationBuilder.DropIndex(
                name: "IX_OrderAccessoryLines_SourceOrderLineId",
                table: "OrderAccessoryLines");

            migrationBuilder.DropColumn(
                name: "SourceOrderLineId",
                table: "OrderAccessoryLines");
        }
    }
}
