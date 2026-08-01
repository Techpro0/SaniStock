using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaniStock.Data.Migrations
{
    /// <summary>
    /// Lets a dispatch be reversed, the same way production, packing and receipts already can be:
    /// a linked reversing note rather than an edit or a delete.
    /// <para>
    /// Purely additive — two columns on <c>DispatchEntries</c>, no existing row touched. Historic
    /// dispatches get <c>IsReversal = 0</c> and a null <c>ReversesEntryId</c>, which is exactly what
    /// they are, and they become reversible immediately: the bucket-and-brand split each one used
    /// is read back from the <c>StockMovements</c> it wrote, which every dispatch has always
    /// recorded. No back-fill is needed.
    /// </para>
    /// </summary>
    public partial class AddDispatchReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReversal",
                table: "DispatchEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReversesEntryId",
                table: "DispatchEntries",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsReversal",
                table: "DispatchEntries");

            migrationBuilder.DropColumn(
                name: "ReversesEntryId",
                table: "DispatchEntries");
        }
    }
}
