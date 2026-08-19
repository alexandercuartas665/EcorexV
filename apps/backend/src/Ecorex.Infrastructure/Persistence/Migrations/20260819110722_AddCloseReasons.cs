using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCloseReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "close_reason",
                table: "task_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "close_reasons_json",
                table: "task_boards",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "close_reason",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "close_reasons_json",
                table: "task_boards");
        }
    }
}
