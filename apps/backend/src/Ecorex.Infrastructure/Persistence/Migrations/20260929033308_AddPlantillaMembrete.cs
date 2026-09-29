using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlantillaMembrete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "membrete_html",
                table: "documentos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "header_html",
                table: "document_template_groups",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "membrete_html",
                table: "documentos");

            migrationBuilder.DropColumn(
                name: "header_html",
                table: "document_template_groups");
        }
    }
}
