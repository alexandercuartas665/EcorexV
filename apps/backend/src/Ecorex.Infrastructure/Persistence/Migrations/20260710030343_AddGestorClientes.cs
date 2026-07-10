using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGestorClientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "bolsa_columna_id",
                table: "terceros",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bolsa_columnas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    color = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    es_cliente = table.Column<bool>(type: "boolean", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bolsa_columnas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "oportunidades",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tercero_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    etapa = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    responsable = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    probabilidad = table.Column<int>(type: "integer", nullable: false),
                    fecha_cierre = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fuente = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oportunidades", x => x.id);
                    table.ForeignKey(
                        name: "fk_oportunidades_terceros_tercero_id",
                        column: x => x.tercero_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "prospectos_scrapeados",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fuente = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nombre_completo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cargo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    empresa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ciudad = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    metrica = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    badge = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    telefono = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    correo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    data_json = table.Column<string>(type: "jsonb", nullable: true),
                    tercero_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_captura = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prospectos_scrapeados", x => x.id);
                    table.ForeignKey(
                        name: "fk_prospectos_scrapeados_terceros_tercero_id",
                        column: x => x.tercero_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tercero_filtros",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    fuente = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    criterios_json = table.Column<string>(type: "jsonb", nullable: true),
                    conteo_anterior = table.Column<int>(type: "integer", nullable: false),
                    fecha_snapshot = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tercero_filtros", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "citas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tercero_id = table.Column<Guid>(type: "uuid", nullable: true),
                    oportunidad_id = table.Column<Guid>(type: "uuid", nullable: true),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duracion_minutos = table.Column<int>(type: "integer", nullable: false),
                    nota = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    completada = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_citas", x => x.id);
                    table.ForeignKey(
                        name: "fk_citas_oportunidades_oportunidad_id",
                        column: x => x.oportunidad_id,
                        principalTable: "oportunidades",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_citas_terceros_tercero_id",
                        column: x => x.tercero_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_terceros_bolsa_columna_id",
                table: "terceros",
                column: "bolsa_columna_id");

            migrationBuilder.CreateIndex(
                name: "ix_terceros_tenant_id_bolsa_columna_id",
                table: "terceros",
                columns: new[] { "tenant_id", "bolsa_columna_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bolsa_columnas_tenant_id_is_archived_sort_order",
                table: "bolsa_columnas",
                columns: new[] { "tenant_id", "is_archived", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_citas_oportunidad_id",
                table: "citas",
                column: "oportunidad_id");

            migrationBuilder.CreateIndex(
                name: "ix_citas_tenant_id_inicio",
                table: "citas",
                columns: new[] { "tenant_id", "inicio" });

            migrationBuilder.CreateIndex(
                name: "ix_citas_tenant_id_tercero_id",
                table: "citas",
                columns: new[] { "tenant_id", "tercero_id" });

            migrationBuilder.CreateIndex(
                name: "ix_citas_tercero_id",
                table: "citas",
                column: "tercero_id");

            migrationBuilder.CreateIndex(
                name: "ix_oportunidades_tenant_id_etapa_sort_order",
                table: "oportunidades",
                columns: new[] { "tenant_id", "etapa", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_oportunidades_tenant_id_tercero_id",
                table: "oportunidades",
                columns: new[] { "tenant_id", "tercero_id" });

            migrationBuilder.CreateIndex(
                name: "ix_oportunidades_tercero_id",
                table: "oportunidades",
                column: "tercero_id");

            migrationBuilder.CreateIndex(
                name: "ix_prospectos_scrapeados_tenant_id_fuente",
                table: "prospectos_scrapeados",
                columns: new[] { "tenant_id", "fuente" });

            migrationBuilder.CreateIndex(
                name: "ix_prospectos_scrapeados_tercero_id",
                table: "prospectos_scrapeados",
                column: "tercero_id");

            migrationBuilder.CreateIndex(
                name: "ix_tercero_filtros_tenant_id_sort_order",
                table: "tercero_filtros",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.AddForeignKey(
                name: "fk_terceros_bolsa_columnas_bolsa_columna_id",
                table: "terceros",
                column: "bolsa_columna_id",
                principalTable: "bolsa_columnas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_terceros_bolsa_columnas_bolsa_columna_id",
                table: "terceros");

            migrationBuilder.DropTable(
                name: "bolsa_columnas");

            migrationBuilder.DropTable(
                name: "citas");

            migrationBuilder.DropTable(
                name: "prospectos_scrapeados");

            migrationBuilder.DropTable(
                name: "tercero_filtros");

            migrationBuilder.DropTable(
                name: "oportunidades");

            migrationBuilder.DropIndex(
                name: "ix_terceros_bolsa_columna_id",
                table: "terceros");

            migrationBuilder.DropIndex(
                name: "ix_terceros_tenant_id_bolsa_columna_id",
                table: "terceros");

            migrationBuilder.DropColumn(
                name: "bolsa_columna_id",
                table: "terceros");
        }
    }
}
