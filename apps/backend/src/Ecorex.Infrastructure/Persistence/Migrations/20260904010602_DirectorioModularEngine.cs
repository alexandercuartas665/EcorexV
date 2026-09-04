using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DirectorioModularEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "directory_engine",
                table: "terceros",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Clasico");

            migrationBuilder.AddColumn<bool>(
                name: "read_only",
                table: "tercero_field_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "requerido_en",
                table: "tercero_field_definitions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "aplica_a",
                table: "tercero_ficha_definitions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "areas",
                table: "tercero_ficha_definitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "icono",
                table: "tercero_ficha_definitions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "protegida",
                table: "tercero_ficha_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "directorio_categoria_secciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    categoria_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ficha_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_directorio_categoria_secciones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "directorio_categorias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    categoria_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    icono = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    areas = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    protegido = table.Column<bool>(type: "boolean", nullable: false),
                    homologa_seccion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_hidden = table.Column<bool>(type: "boolean", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_directorio_categorias", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tercero_categorias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tercero_id = table.Column<Guid>(type: "uuid", nullable: false),
                    categoria_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tercero_categorias", x => x.id);
                    table.ForeignKey(
                        name: "fk_tercero_categorias_terceros_tercero_id",
                        column: x => x.tercero_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_directorio_categoria_secciones_tenant_id_categoria_key_fich",
                table: "directorio_categoria_secciones",
                columns: new[] { "tenant_id", "categoria_key", "ficha_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_directorio_categoria_secciones_tenant_id_categoria_key_orden",
                table: "directorio_categoria_secciones",
                columns: new[] { "tenant_id", "categoria_key", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_directorio_categorias_tenant_id_categoria_key",
                table: "directorio_categorias",
                columns: new[] { "tenant_id", "categoria_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_directorio_categorias_tenant_id_sort_order",
                table: "directorio_categorias",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_tercero_categorias_tenant_id_categoria_key",
                table: "tercero_categorias",
                columns: new[] { "tenant_id", "categoria_key" });

            migrationBuilder.CreateIndex(
                name: "ix_tercero_categorias_tenant_id_tercero_id_categoria_key",
                table: "tercero_categorias",
                columns: new[] { "tenant_id", "tercero_id", "categoria_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tercero_categorias_tercero_id",
                table: "tercero_categorias",
                column: "tercero_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "directorio_categoria_secciones");

            migrationBuilder.DropTable(
                name: "directorio_categorias");

            migrationBuilder.DropTable(
                name: "tercero_categorias");

            migrationBuilder.DropColumn(
                name: "directory_engine",
                table: "terceros");

            migrationBuilder.DropColumn(
                name: "read_only",
                table: "tercero_field_definitions");

            migrationBuilder.DropColumn(
                name: "requerido_en",
                table: "tercero_field_definitions");

            migrationBuilder.DropColumn(
                name: "aplica_a",
                table: "tercero_ficha_definitions");

            migrationBuilder.DropColumn(
                name: "areas",
                table: "tercero_ficha_definitions");

            migrationBuilder.DropColumn(
                name: "icono",
                table: "tercero_ficha_definitions");

            migrationBuilder.DropColumn(
                name: "protegida",
                table: "tercero_ficha_definitions");
        }
    }
}
