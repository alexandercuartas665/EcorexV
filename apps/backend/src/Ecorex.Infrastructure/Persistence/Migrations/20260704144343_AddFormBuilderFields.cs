using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
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
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_hidden",
                table: "form_questions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_locked",
                table: "form_questions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "placeholder_text",
                table: "form_questions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "width",
                table: "form_questions",
                type: "integer",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<bool>(
                name: "is_hidden",
                table: "form_containers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_locked",
                table: "form_containers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "tabs_json",
                table: "form_containers",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "width",
                table: "form_containers",
                type: "integer",
                nullable: false,
                defaultValue: 12);

            // Backfill (ADR-0021): deriva width del grid_col existente (col-12 / col-md-N)
            // para que las definiciones previas conserven su layout en el constructor.
            migrationBuilder.Sql("""
                UPDATE form_questions
                SET width = CAST(substring(grid_col FROM '([0-9]+)$') AS integer)
                WHERE grid_col ~ '(^|-)([1-9]|1[0-2])$';
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
