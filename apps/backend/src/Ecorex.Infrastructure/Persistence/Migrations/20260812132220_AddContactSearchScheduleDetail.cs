using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactSearchScheduleDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "day_of_month",
                table: "contact_search_definitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "day_of_week",
                table: "contact_search_definitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "run_time",
                table: "contact_search_definitions",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "day_of_month",
                table: "contact_search_definitions");

            migrationBuilder.DropColumn(
                name: "day_of_week",
                table: "contact_search_definitions");

            migrationBuilder.DropColumn(
                name: "run_time",
                table: "contact_search_definitions");
        }
    }
}
