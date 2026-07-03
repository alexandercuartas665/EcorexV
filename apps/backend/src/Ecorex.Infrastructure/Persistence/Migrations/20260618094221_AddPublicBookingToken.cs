using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicBookingToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "online_booking_enabled",
                table: "tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "public_booking_token",
                table: "tenants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_public_booking_token",
                table: "tenants",
                column: "public_booking_token",
                unique: true,
                filter: "public_booking_token IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tenants_public_booking_token",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "online_booking_enabled",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "public_booking_token",
                table: "tenants");
        }
    }
}
