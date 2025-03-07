using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketsApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinceInTableTicket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Valeu",
                table: "Tickets",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Valeu",
                table: "Tickets");
        }
    }
}
