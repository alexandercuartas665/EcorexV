using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalisisLibros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analisis_libros",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    descripcion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    visibilidad = table.Column<int>(type: "int", nullable: false),
                    allowed_user_ids_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("pk_analisis_libros", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "analisis_hojas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    libro_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    orden = table.Column<int>(type: "int", nullable: false),
                    filtros_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("pk_analisis_hojas", x => x.id);
                    table.ForeignKey(
                        name: "fk_analisis_hojas_analisis_libros_libro_id",
                        column: x => x.libro_id,
                        principalTable: "analisis_libros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "analisis_libro_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    libro_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analisis_libro_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_analisis_libro_tokens_analisis_libros_libro_id",
                        column: x => x.libro_id,
                        principalTable: "analisis_libros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "analisis_widgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    hoja_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tipo = table.Column<int>(type: "int", nullable: false),
                    titulo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    spec_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    indicador_key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    config_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    context_bindings_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    orden = table.Column<int>(type: "int", nullable: false),
                    col_span = table.Column<int>(type: "int", nullable: false),
                    row_span = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analisis_widgets", x => x.id);
                    table.ForeignKey(
                        name: "fk_analisis_widgets_analisis_hojas_hoja_id",
                        column: x => x.hoja_id,
                        principalTable: "analisis_hojas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_analisis_hojas_libro_id",
                table: "analisis_hojas",
                column: "libro_id");

            migrationBuilder.CreateIndex(
                name: "ix_analisis_hojas_tenant_id_libro_id_orden",
                table: "analisis_hojas",
                columns: new[] { "tenant_id", "libro_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_analisis_libro_tokens_libro_id",
                table: "analisis_libro_tokens",
                column: "libro_id");

            migrationBuilder.CreateIndex(
                name: "ix_analisis_libro_tokens_tenant_id_libro_id",
                table: "analisis_libro_tokens",
                columns: new[] { "tenant_id", "libro_id" });

            migrationBuilder.CreateIndex(
                name: "ix_analisis_libro_tokens_token_hash",
                table: "analisis_libro_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_analisis_libros_tenant_id_nombre",
                table: "analisis_libros",
                columns: new[] { "tenant_id", "nombre" });

            migrationBuilder.CreateIndex(
                name: "ix_analisis_widgets_hoja_id",
                table: "analisis_widgets",
                column: "hoja_id");

            migrationBuilder.CreateIndex(
                name: "ix_analisis_widgets_tenant_id_hoja_id_orden",
                table: "analisis_widgets",
                columns: new[] { "tenant_id", "hoja_id", "orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analisis_libro_tokens");

            migrationBuilder.DropTable(
                name: "analisis_widgets");

            migrationBuilder.DropTable(
                name: "analisis_hojas");

            migrationBuilder.DropTable(
                name: "analisis_libros");
        }
    }
}
