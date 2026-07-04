using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddFormBuilderFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "default_value",
                table: "form_questions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_hidden",
                table: "form_questions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_locked",
                table: "form_questions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "placeholder_text",
                table: "form_questions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "width",
                table: "form_questions",
                type: "int",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<bool>(
                name: "is_hidden",
                table: "form_containers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_locked",
                table: "form_containers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "tabs_json",
                table: "form_containers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "width",
                table: "form_containers",
                type: "int",
                nullable: false,
                defaultValue: 12);

            // Backfill (ADR-0021): deriva width del grid_col existente (col-12 / col-md-N)
            // para que las definiciones previas conserven su layout en el constructor.
            migrationBuilder.Sql("""
                UPDATE form_questions
                SET width = TRY_CAST(
                    CASE WHEN grid_col LIKE '%-%'
                         THEN RIGHT(grid_col, CHARINDEX('-', REVERSE(grid_col)) - 1)
                         ELSE grid_col END AS int)
                WHERE TRY_CAST(
                    CASE WHEN grid_col LIKE '%-%'
                         THEN RIGHT(grid_col, CHARINDEX('-', REVERSE(grid_col)) - 1)
                         ELSE grid_col END AS int) BETWEEN 1 AND 12;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_value",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "is_hidden",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "is_locked",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "placeholder_text",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "width",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "is_hidden",
                table: "form_containers");

            migrationBuilder.DropColumn(
                name: "is_locked",
                table: "form_containers");

            migrationBuilder.DropColumn(
                name: "tabs_json",
                table: "form_containers");

            migrationBuilder.DropColumn(
                name: "width",
                table: "form_containers");
        }
    }
}
