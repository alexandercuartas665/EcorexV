using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddScrapeStepForEach : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ingest_path",
                table: "scrape_steps",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_loop_end",
                table: "scrape_steps",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "loop_over_var",
                table: "scrape_steps",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ingest_path",
                table: "scrape_steps");

            migrationBuilder.DropColumn(
                name: "is_loop_end",
                table: "scrape_steps");

            migrationBuilder.DropColumn(
                name: "loop_over_var",
                table: "scrape_steps");
        }
    }
}
