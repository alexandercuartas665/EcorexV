using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentoAnalisisProceso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "agente_version_at",
                table: "documentos_analisis",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "input_tokens",
                table: "documentos_analisis",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "modelo",
                table: "documentos_analisis",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "output_tokens",
                table: "documentos_analisis",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "paquetes_count",
                table: "documentos_analisis",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "procesado_at",
                table: "documentos_analisis",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "procesado_por_agent_id",
                table: "documentos_analisis",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "procesamiento_ms",
                table: "documentos_analisis",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "proveedor",
                table: "documentos_analisis",
                type: "int",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resultado_json",
                table: "documentos_analisis",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "documentos_analisis_metadata_campos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    clave = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    etiqueta = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    orden = table.Column<int>(type: "int", nullable: false),
                    activo = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentos_analisis_metadata_campos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documentos_analisis_paquetes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    documento_analisis_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    orden = table.Column<int>(type: "int", nullable: false),
                    nombre_sugerido = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    stored_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pagina_inicio = table.Column<int>(type: "int", nullable: false),
                    pagina_fin = table.Column<int>(type: "int", nullable: false),
                    paginas = table.Column<int>(type: "int", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    confianza = table.Column<double>(type: "float", nullable: true),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    documentos_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentos_analisis_paquetes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documentos_analisis_metadata_campos_tenant_id_clave",
                table: "documentos_analisis_metadata_campos",
                columns: new[] { "tenant_id", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documentos_analisis_paquetes_tenant_id_documento_analisis_id_orden",
                table: "documentos_analisis_paquetes",
                columns: new[] { "tenant_id", "documento_analisis_id", "orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documentos_analisis_metadata_campos");

            migrationBuilder.DropTable(
                name: "documentos_analisis_paquetes");

            migrationBuilder.DropColumn(
                name: "agente_version_at",
                table: "documentos_analisis");

            migrationBuilder.DropColumn(
                name: "input_tokens",
                table: "documentos_analisis");

            migrationBuilder.DropColumn(
                name: "modelo",
                table: "documentos_analisis");

            migrationBuilder.DropColumn(
                name: "output_tokens",
                table: "documentos_analisis");

            migrationBuilder.DropColumn(
                name: "paquetes_count",
                table: "documentos_analisis");

            migrationBuilder.DropColumn(
                name: "procesado_at",
                table: "documentos_analisis");

            migrationBuilder.DropColumn(
                name: "procesado_por_agent_id",
                table: "documentos_analisis");

            migrationBuilder.DropColumn(
                name: "procesamiento_ms",
                table: "documentos_analisis");

            migrationBuilder.DropColumn(
                name: "proveedor",
                table: "documentos_analisis");

            migrationBuilder.DropColumn(
                name: "resultado_json",
                table: "documentos_analisis");
        }
    }
}
