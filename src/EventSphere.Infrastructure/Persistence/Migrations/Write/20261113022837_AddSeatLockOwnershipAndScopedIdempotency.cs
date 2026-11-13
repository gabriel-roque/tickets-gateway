using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventSphere.Infrastructure.Persistence.Migrations.Write
{
    /// <inheritdoc />
    public partial class AddSeatLockOwnershipAndScopedIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "SeatLocks",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "InitiatorId",
                table: "LifecycleWorkflows",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                table: "Idempotency",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RawKey",
                table: "Idempotency",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            // Pre-scope rows keep their raw primary key. The application dual-reads them and
            // derives their request fingerprint from the associated Saga during replay.
            migrationBuilder.Sql("""
                UPDATE [Idempotency]
                SET [RawKey] = [Key]
                WHERE [RawKey] = N'' AND [Fingerprint] = N'';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SeatLocks_OwnerId",
                table: "SeatLocks",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_LifecycleWorkflows_InitiatorId",
                table: "LifecycleWorkflows",
                column: "InitiatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Idempotency_RawKey",
                table: "Idempotency",
                column: "RawKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SeatLocks_OwnerId",
                table: "SeatLocks");

            migrationBuilder.DropIndex(
                name: "IX_LifecycleWorkflows_InitiatorId",
                table: "LifecycleWorkflows");

            migrationBuilder.DropIndex(
                name: "IX_Idempotency_RawKey",
                table: "Idempotency");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "SeatLocks");

            migrationBuilder.DropColumn(
                name: "InitiatorId",
                table: "LifecycleWorkflows");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                table: "Idempotency");

            migrationBuilder.DropColumn(
                name: "RawKey",
                table: "Idempotency");
        }
    }
}
