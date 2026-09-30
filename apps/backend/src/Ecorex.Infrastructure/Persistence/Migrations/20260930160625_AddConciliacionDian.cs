using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConciliacionDian : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conciliacion_dian_bot_dummies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cufe = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo_doc = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    fecha_doc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    nombre_emisor = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    nit_emisor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    prefijo_folio = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    id_compra = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    subtotal_bruto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    descuento_comercial = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_antes_ret = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    retencion_fuente = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    retencion_ica = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tipo_pago = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    proveedor_tecnologico = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion_dian_bot_dummies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conciliacion_dian_documentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    consecutivo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    mes = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion_dian_documentos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conciliacion_dian_erp_ref_dummies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    referencia = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    documento_interno = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion_dian_erp_ref_dummies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conciliacion_dian_newton_dummies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cufe = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    guid_pdf = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion_dian_newton_dummies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conciliacion_dian_renglones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    cufe = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo_doc_dian = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    fecha_emision = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    nombre_proveedor = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    nit_proveedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    num_factura_proveedor = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    num_factura_soldarco = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    orden_compra_soldarco = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    subtotal_bruto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    descuento_comercial = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    subtotal_neto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    iva_descontable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_antes_retenciones = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ret_retefuente = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ret_ica = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_factura = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tipo_pago = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    factura_aprobada = table.Column<bool>(type: "boolean", nullable: false),
                    aprobada_manual = table.Column<bool>(type: "boolean", nullable: false),
                    plataforma_proveedor = table.Column<bool>(type: "boolean", nullable: false),
                    rut_escaneado = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    evento30 = table.Column<bool>(type: "boolean", nullable: false),
                    evento31 = table.Column<bool>(type: "boolean", nullable: false),
                    evento32 = table.Column<bool>(type: "boolean", nullable: false),
                    evento33 = table.Column<bool>(type: "boolean", nullable: false),
                    evento34 = table.Column<bool>(type: "boolean", nullable: false),
                    seleccionado = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion_dian_renglones", x => x.id);
                    table.ForeignKey(
                        name: "fk_conciliacion_dian_renglones_conciliacion_dian_documentos_do",
                        column: x => x.documento_id,
                        principalTable: "conciliacion_dian_documentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_dian_bot_dummies_tenant_id_cufe",
                table: "conciliacion_dian_bot_dummies",
                columns: new[] { "tenant_id", "cufe" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_dian_documentos_tenant_id_anio_mes",
                table: "conciliacion_dian_documentos",
                columns: new[] { "tenant_id", "anio", "mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_dian_documentos_tenant_id_consecutivo",
                table: "conciliacion_dian_documentos",
                columns: new[] { "tenant_id", "consecutivo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_dian_erp_ref_dummies_tenant_id_referencia",
                table: "conciliacion_dian_erp_ref_dummies",
                columns: new[] { "tenant_id", "referencia" });

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_dian_newton_dummies_tenant_id_cufe",
                table: "conciliacion_dian_newton_dummies",
                columns: new[] { "tenant_id", "cufe" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_dian_renglones_documento_id",
                table: "conciliacion_dian_renglones",
                column: "documento_id");

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_dian_renglones_tenant_id_documento_id_cufe",
                table: "conciliacion_dian_renglones",
                columns: new[] { "tenant_id", "documento_id", "cufe" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_dian_renglones_tenant_id_documento_id_estado",
                table: "conciliacion_dian_renglones",
                columns: new[] { "tenant_id", "documento_id", "estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conciliacion_dian_bot_dummies");

            migrationBuilder.DropTable(
                name: "conciliacion_dian_erp_ref_dummies");

            migrationBuilder.DropTable(
                name: "conciliacion_dian_newton_dummies");

            migrationBuilder.DropTable(
                name: "conciliacion_dian_renglones");

            migrationBuilder.DropTable(
                name: "conciliacion_dian_documentos");
        }
    }
}
