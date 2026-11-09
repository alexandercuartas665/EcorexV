using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibilidad = table.Column<int>(type: "integer", nullable: false),
                    allowed_user_ids_json = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("pk_analisis_libros", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "analisis_hojas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    libro_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    filtros_json = table.Column<string>(type: "text", nullable: true),
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    libro_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hoja_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    titulo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    spec_json = table.Column<string>(type: "text", nullable: true),
                    indicador_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    config_json = table.Column<string>(type: "text", nullable: true),
                    context_bindings_json = table.Column<string>(type: "text", nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    col_span = table.Column<int>(type: "integer", nullable: false),
                    row_span = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
