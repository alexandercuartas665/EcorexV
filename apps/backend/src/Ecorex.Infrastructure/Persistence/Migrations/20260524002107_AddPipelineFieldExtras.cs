using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineFieldExtras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allow_multiple",
                table: "pipeline_field_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "pipeline_field_definitions",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "repeat_with_field_key",
                table: "pipeline_field_definitions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allow_multiple",
                table: "pipeline_field_definitions");

            migrationBuilder.DropColumn(
                name: "description",
                table: "pipeline_field_definitions");

            migrationBuilder.DropColumn(
                name: "repeat_with_field_key",
                table: "pipeline_field_definitions");
        }
    }
}
