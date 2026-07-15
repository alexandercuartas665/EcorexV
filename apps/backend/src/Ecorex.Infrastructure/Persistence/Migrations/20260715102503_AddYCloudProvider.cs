using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddYCloudProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "y_cloud_api_key_encrypted",
                table: "whats_app_lines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "y_cloud_phone_number_id",
                table: "whats_app_lines",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "y_cloud_waba_id",
                table: "whats_app_lines",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_whats_app_lines_y_cloud_phone_number_id",
                table: "whats_app_lines",
                column: "y_cloud_phone_number_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_whats_app_lines_y_cloud_phone_number_id",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "y_cloud_api_key_encrypted",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "y_cloud_phone_number_id",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "y_cloud_waba_id",
                table: "whats_app_lines");
        }
    }
}
