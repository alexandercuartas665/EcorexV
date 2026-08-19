using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProspectoDireccionSitioWeb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "direccion",
                table: "prospectos_scrapeados",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sitio_web",
                table: "prospectos_scrapeados",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "direccion",
                table: "prospectos_scrapeados");

            migrationBuilder.DropColumn(
                name: "sitio_web",
                table: "prospectos_scrapeados");
        }
    }
}
