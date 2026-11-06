using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequerimientoRfq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requerimiento_rfqs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor_contacto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    form_response_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    fecha_envio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_respuesta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    vigencia_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    solicitante_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    solicitante_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notas = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requerimiento_rfqs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "requerimiento_rfq_lineas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rfq_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requerimiento_compra_hijo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requerimiento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave_producto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    descripcion = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unidad_medida = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    fecha_requerida = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    precio_unitario = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    vigencia_dias = table.Column<int>(type: "integer", nullable: true),
                    lead_time = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    marca = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    nota = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requerimiento_rfq_lineas", x => x.id);
                    table.ForeignKey(
                        name: "fk_requerimiento_rfq_lineas_requerimiento_rfqs_rfq_id",
                        column: x => x.rfq_id,
                        principalTable: "requerimiento_rfqs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_requerimiento_rfq_lineas_rfq_id",
                table: "requerimiento_rfq_lineas",
                column: "rfq_id");

            migrationBuilder.CreateIndex(
                name: "ix_requerimiento_rfq_lineas_tenant_id_requerimiento_compra_hij",
                table: "requerimiento_rfq_lineas",
                columns: new[] { "tenant_id", "requerimiento_compra_hijo_id" });

            migrationBuilder.CreateIndex(
                name: "ix_requerimiento_rfq_lineas_tenant_id_rfq_id",
                table: "requerimiento_rfq_lineas",
                columns: new[] { "tenant_id", "rfq_id" });

            migrationBuilder.CreateIndex(
                name: "ix_requerimiento_rfqs_tenant_id_estado",
                table: "requerimiento_rfqs",
                columns: new[] { "tenant_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_requerimiento_rfqs_tenant_id_numero",
                table: "requerimiento_rfqs",
                columns: new[] { "tenant_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requerimiento_rfqs_tenant_id_proveedor_id",
                table: "requerimiento_rfqs",
                columns: new[] { "tenant_id", "proveedor_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requerimiento_rfq_lineas");

            migrationBuilder.DropTable(
                name: "requerimiento_rfqs");
        }
    }
}
