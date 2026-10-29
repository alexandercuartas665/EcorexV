using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FormContainerFillFreeWidth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hidden_surfaces_json",
                table: "form_containers");

            migrationBuilder.AddColumn<bool>(
                name: "fill_free_width",
                table: "form_containers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fill_free_width",
                table: "form_containers");

            migrationBuilder.AddColumn<string>(
                name: "hidden_surfaces_json",
                table: "form_containers",
                type: "jsonb",
                nullable: true);
        }
    }
}
