using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddComprasIvaDescuento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "descuento_pct",
                table: "requerimiento_cotizaciones",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "descuento_valor",
                table: "requerimiento_cotizaciones",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "iva_pct",
                table: "requerimiento_cotizaciones",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "descuento_pct",
                table: "requerimiento_compra_hijos",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "descuento_valor",
                table: "requerimiento_compra_hijos",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "iva_pct",
                table: "requerimiento_compra_hijos",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "iva_rate",
                table: "items",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "descuento_pct",
                table: "requerimiento_cotizaciones");

            migrationBuilder.DropColumn(
                name: "descuento_valor",
                table: "requerimiento_cotizaciones");

            migrationBuilder.DropColumn(
                name: "iva_pct",
                table: "requerimiento_cotizaciones");

            migrationBuilder.DropColumn(
                name: "descuento_pct",
                table: "requerimiento_compra_hijos");

            migrationBuilder.DropColumn(
                name: "descuento_valor",
                table: "requerimiento_compra_hijos");

            migrationBuilder.DropColumn(
                name: "iva_pct",
                table: "requerimiento_compra_hijos");

            migrationBuilder.DropColumn(
                name: "iva_rate",
                table: "items");
        }
    }
}
