using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactEnrichFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "frase_busqueda",
                table: "prospectos_scrapeados",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "enrich_linked_in",
                table: "contact_search_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "enrich_max_por_empresa",
                table: "contact_search_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "frase_busqueda",
                table: "prospectos_scrapeados");

            migrationBuilder.DropColumn(
                name: "enrich_linked_in",
                table: "contact_search_definitions");

            migrationBuilder.DropColumn(
                name: "enrich_max_por_empresa",
                table: "contact_search_definitions");
        }
    }
}
