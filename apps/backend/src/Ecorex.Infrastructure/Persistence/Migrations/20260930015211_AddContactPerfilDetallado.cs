using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactPerfilDetallado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "perfil_detalle",
                table: "prospectos_scrapeados",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "perfil_detallado",
                table: "contact_search_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "perfil_detallado_max",
                table: "contact_search_definitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "perfil_detalle",
                table: "prospectos_scrapeados");

            migrationBuilder.DropColumn(
                name: "perfil_detallado",
                table: "contact_search_definitions");

            migrationBuilder.DropColumn(
                name: "perfil_detallado_max",
                table: "contact_search_definitions");
        }
    }
}
