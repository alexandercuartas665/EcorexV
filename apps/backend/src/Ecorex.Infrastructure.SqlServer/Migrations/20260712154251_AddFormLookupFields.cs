using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddFormLookupFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "autofill_map_json",
                table: "form_questions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_field",
                table: "form_questions",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "filter_json",
                table: "form_questions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "presentation",
                table: "form_questions",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Autocomplete");

            migrationBuilder.AddColumn<string>(
                name: "source_kind",
                table: "form_questions",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Options");

            migrationBuilder.AddColumn<string>(
                name: "source_ref",
                table: "form_questions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "value_field",
                table: "form_questions",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "autofill_map_json",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "display_field",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "filter_json",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "presentation",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "source_kind",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "source_ref",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "value_field",
                table: "form_questions");
        }
    }
}
