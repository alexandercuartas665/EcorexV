using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddRequerimientoCotizaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requerimiento_cotizaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    clave_producto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    proveedor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    proveedor_contacto_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    precio_unitario = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    tiempo_entrega = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    caracteristicas_despacho = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    condiciones_pago = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    vigencia_hasta = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    notas = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    fuente = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requerimiento_cotizaciones", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_requerimiento_cotizaciones_tenant_id_clave_producto",
                table: "requerimiento_cotizaciones",
                columns: new[] { "tenant_id", "clave_producto" });

            migrationBuilder.CreateIndex(
                name: "ix_requerimiento_cotizaciones_tenant_id_clave_producto_proveedor_id",
                table: "requerimiento_cotizaciones",
                columns: new[] { "tenant_id", "clave_producto", "proveedor_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requerimiento_cotizaciones");
        }
    }
}
