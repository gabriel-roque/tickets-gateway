using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketsApi.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTableEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "Events");

            migrationBuilder.AddColumn<int>(
                name: "PriceTicket",
                table: "Events",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceTicket",
                table: "Events");

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Events",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
