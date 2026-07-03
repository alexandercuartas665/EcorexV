using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "webhook_active_url",
                table: "evolution_master_configs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "webhook_mode",
                table: "evolution_master_configs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Development");

            migrationBuilder.AddColumn<string>(
                name: "webhook_public_url",
                table: "evolution_master_configs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "webhook_token",
                table: "evolution_master_configs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "webhook_active_url",
                table: "evolution_master_configs");

            migrationBuilder.DropColumn(
                name: "webhook_mode",
                table: "evolution_master_configs");

            migrationBuilder.DropColumn(
                name: "webhook_public_url",
                table: "evolution_master_configs");

            migrationBuilder.DropColumn(
                name: "webhook_token",
                table: "evolution_master_configs");
        }
    }
}
