using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequerimientoCotizacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "subcategoria_cotizacion_id",
                table: "requerimiento_origenes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cotizacion_task_item_id",
                table: "requerimiento_compra_hijos",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "subcategoria_cotizacion_id",
                table: "requerimiento_origenes");

            migrationBuilder.DropColumn(
                name: "cotizacion_task_item_id",
                table: "requerimiento_compra_hijos");
        }
    }
}
