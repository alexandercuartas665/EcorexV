using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentoAnalisis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documentos_analisis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stored_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    extension = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    content_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    motivo_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentos_analisis", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documentos_analisis_tenant_id_lote_id",
                table: "documentos_analisis",
                columns: new[] { "tenant_id", "lote_id" });

            migrationBuilder.CreateIndex(
                name: "ix_documentos_analisis_tenant_id_stored_file_id",
                table: "documentos_analisis",
                columns: new[] { "tenant_id", "stored_file_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documentos_analisis");
        }
    }
}
