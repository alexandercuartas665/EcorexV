using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class CamposCalculadosYAnchos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "formula",
                table: "tercero_field_definitions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "repeat_with_field_key",
                table: "tercero_field_definitions",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "show_in_filter",
                table: "tercero_field_definitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "formula",
                table: "item_field_definitions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "repeat_with_field_key",
                table: "item_field_definitions",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "show_in_filter",
                table: "item_field_definitions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "formula",
                table: "tercero_field_definitions");

            migrationBuilder.DropColumn(
                name: "repeat_with_field_key",
                table: "tercero_field_definitions");

            migrationBuilder.DropColumn(
                name: "show_in_filter",
                table: "tercero_field_definitions");

            migrationBuilder.DropColumn(
                name: "formula",
                table: "item_field_definitions");

            migrationBuilder.DropColumn(
                name: "repeat_with_field_key",
                table: "item_field_definitions");

            migrationBuilder.DropColumn(
                name: "show_in_filter",
                table: "item_field_definitions");
        }
    }
}
