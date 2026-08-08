using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaniStock.Data.Migrations
{
    /// <summary>
    /// Lets an accessory receipt name a brand and land directly on that brand's packed stock —
    /// for goods that arrive already packaged (an outside import, for instance) — instead of
    /// always landing in the shared unpacked pool and needing a separate trip to Packing.
    /// Purely additive: the new column is nullable, so every existing receipt defaults to null,
    /// which is exactly what it already meant before this column existed.
    /// </summary>
    public partial class AddAccessoryReceiptBrand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BrandId",
                table: "AccessoryReceipts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessoryReceipts_BrandId",
                table: "AccessoryReceipts",
                column: "BrandId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessoryReceipts_Brands_BrandId",
                table: "AccessoryReceipts",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessoryReceipts_Brands_BrandId",
                table: "AccessoryReceipts");

            migrationBuilder.DropIndex(
                name: "IX_AccessoryReceipts_BrandId",
                table: "AccessoryReceipts");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "AccessoryReceipts");
        }
    }
}
