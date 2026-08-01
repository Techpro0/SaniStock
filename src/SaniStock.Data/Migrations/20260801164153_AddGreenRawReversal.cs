using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaniStock.Data.Migrations
{
    /// <summary>
    /// Gives green-ware and raw-material entries the same reversal pair every other posting in the
    /// app already had, so a mistake there can be corrected the same way: by writing a linked
    /// reversing row rather than editing or removing anything.
    /// <para>
    /// Purely additive — two columns per table, no existing row is touched. Historic entries get
    /// <c>IsReversal = 0</c> and a null <c>ReversesEntryId</c>, which is exactly what they are:
    /// ordinary postings that have never been reversed.
    /// </para>
    /// </summary>
    public partial class AddGreenRawReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReversal",
                table: "RawMaterialEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReversesEntryId",
                table: "RawMaterialEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReversal",
                table: "GreenPieceEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReversesEntryId",
                table: "GreenPieceEntries",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsReversal",
                table: "RawMaterialEntries");

            migrationBuilder.DropColumn(
                name: "ReversesEntryId",
                table: "RawMaterialEntries");

            migrationBuilder.DropColumn(
                name: "IsReversal",
                table: "GreenPieceEntries");

            migrationBuilder.DropColumn(
                name: "ReversesEntryId",
                table: "GreenPieceEntries");
        }
    }
}
