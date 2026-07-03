using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppCloudProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cloud_access_token_encrypted",
                table: "whats_app_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cloud_business_account_id",
                table: "whats_app_lines",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cloud_phone_number_id",
                table: "whats_app_lines",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "whats_app_lines",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "meta_webhook_verify_token",
                table: "evolution_master_configs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_whats_app_lines_cloud_phone_number_id",
                table: "whats_app_lines",
                column: "cloud_phone_number_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_whats_app_lines_cloud_phone_number_id",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "cloud_access_token_encrypted",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "cloud_business_account_id",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "cloud_phone_number_id",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "provider",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "meta_webhook_verify_token",
                table: "evolution_master_configs");
        }
    }
}
