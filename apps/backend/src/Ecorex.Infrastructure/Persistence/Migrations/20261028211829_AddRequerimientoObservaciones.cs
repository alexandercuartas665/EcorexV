using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequerimientoObservaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "observaciones",
                table: "requerimientos_compra",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "observaciones_despacho",
                table: "requerimientos_compra",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "observaciones",
                table: "requerimientos_compra");

            migrationBuilder.DropColumn(
                name: "observaciones_despacho",
                table: "requerimientos_compra");
        }
    }
}
