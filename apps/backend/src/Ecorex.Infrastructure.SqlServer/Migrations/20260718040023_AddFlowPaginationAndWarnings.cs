using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddFlowPaginationAndWarnings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "warning_action",
                table: "scrape_steps",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "warning_label",
                table: "scrape_steps",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "page_from",
                table: "scrape_flows",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "page_to",
                table: "scrape_flows",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "page_var",
                table: "scrape_flows",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "warning_action",
                table: "scrape_steps");

            migrationBuilder.DropColumn(
                name: "warning_label",
                table: "scrape_steps");

            migrationBuilder.DropColumn(
                name: "page_from",
                table: "scrape_flows");

            migrationBuilder.DropColumn(
                name: "page_to",
                table: "scrape_flows");

            migrationBuilder.DropColumn(
                name: "page_var",
                table: "scrape_flows");
        }
    }
}
