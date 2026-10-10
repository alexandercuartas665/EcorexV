using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequerimientosCompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requerimiento_origenes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    area = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    tipo_fuente = table.Column<int>(type: "integer", nullable: false),
                    fuente_ref_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subcategoria_compra_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requerimiento_origenes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "requerimiento_proveedor_reglas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dimension = table.Column<int>(type: "integer", nullable: false),
                    valor_dimension = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prioridad = table.Column<int>(type: "integer", nullable: false),
                    aprendida = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requerimiento_proveedor_reglas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "requerimientos_compra",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    area = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    descripcion = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unidad_medida = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    fecha_necesidad = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    clave_agrupacion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requerimientos_compra", x => x.id);
                    table.ForeignKey(
                        name: "fk_requerimientos_compra_requerimiento_origenes_origen_id",
                        column: x => x.origen_id,
                        principalTable: "requerimiento_origenes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "requerimiento_compra_hijos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    requerimiento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    fecha_programada_compra = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    caracteristicas_despacho = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    tiempo_entrega = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    task_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requerimiento_compra_hijos", x => x.id);
                    table.ForeignKey(
                        name: "fk_requerimiento_compra_hijos_requerimientos_compra_requerimie",
                        column: x => x.requerimiento_id,
                        principalTable: "requerimientos_compra",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_requerimiento_compra_hijos_requerimiento_id",
                table: "requerimiento_compra_hijos",
                column: "requerimiento_id");

            migrationBuilder.CreateIndex(
                name: "ix_requerimiento_compra_hijos_tenant_id_requerimiento_id",
                table: "requerimiento_compra_hijos",
                columns: new[] { "tenant_id", "requerimiento_id" });

            migrationBuilder.CreateIndex(
                name: "ix_requerimiento_origenes_tenant_id_nombre",
                table: "requerimiento_origenes",
                columns: new[] { "tenant_id", "nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requerimiento_proveedor_reglas_tenant_id_dimension_valor_di",
                table: "requerimiento_proveedor_reglas",
                columns: new[] { "tenant_id", "dimension", "valor_dimension", "proveedor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requerimientos_compra_origen_id",
                table: "requerimientos_compra",
                column: "origen_id");

            migrationBuilder.CreateIndex(
                name: "ix_requerimientos_compra_tenant_id_clave_agrupacion",
                table: "requerimientos_compra",
                columns: new[] { "tenant_id", "clave_agrupacion" });

            migrationBuilder.CreateIndex(
                name: "ix_requerimientos_compra_tenant_id_estado",
                table: "requerimientos_compra",
                columns: new[] { "tenant_id", "estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requerimiento_compra_hijos");

            migrationBuilder.DropTable(
                name: "requerimiento_proveedor_reglas");

            migrationBuilder.DropTable(
                name: "requerimientos_compra");

            migrationBuilder.DropTable(
                name: "requerimiento_origenes");
        }
    }
}
