using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntidadConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entidad_field_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    field_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    options = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    column = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entidad_field_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entidades",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nombre = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    nombre_comercial = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    sigla = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    tipo_entidad = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    tax_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    tax_id_dv = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    representante_legal = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    naturaleza_juridica = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    pais = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    departamento = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ciudad = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    direccion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    telefono = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    web = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    zona_horaria = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    idioma = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    observaciones = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    logo_base64 = table.Column<string>(type: "text", nullable: true),
                    is_principal = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    field_values_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entidades", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entidad_field_definitions_tenant_id_field_key",
                table: "entidad_field_definitions",
                columns: new[] { "tenant_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entidad_field_definitions_tenant_id_sort_order",
                table: "entidad_field_definitions",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_entidades_tenant_id_codigo",
                table: "entidades",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entidades_tenant_id_is_archived",
                table: "entidades",
                columns: new[] { "tenant_id", "is_archived" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entidad_field_definitions");

            migrationBuilder.DropTable(
                name: "entidades");
        }
    }
}
