using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectorioGeneral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "terceros",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    perfiles = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    vendedor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ciudad = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    id_tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    id_valor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sector = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    cargo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    telefono = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fichas_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terceros", x => x.id);
                    table.ForeignKey(
                        name: "fk_terceros_terceros_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tercero_contactos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tercero_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cargo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    telefono = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tercero_contactos", x => x.id);
                    table.ForeignKey(
                        name: "fk_tercero_contactos_terceros_tercero_id",
                        column: x => x.tercero_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tercero_contactos_tercero_id",
                table: "tercero_contactos",
                column: "tercero_id");

            migrationBuilder.CreateIndex(
                name: "ix_terceros_empresa_id",
                table: "terceros",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_terceros_nombre",
                table: "terceros",
                column: "nombre");

            migrationBuilder.CreateIndex(
                name: "ix_terceros_tenant_id_empresa_id",
                table: "terceros",
                columns: new[] { "tenant_id", "empresa_id" });

            migrationBuilder.CreateIndex(
                name: "ix_terceros_tenant_id_estado",
                table: "terceros",
                columns: new[] { "tenant_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_terceros_tenant_id_tipo",
                table: "terceros",
                columns: new[] { "tenant_id", "tipo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tercero_contactos");

            migrationBuilder.DropTable(
                name: "terceros");
        }
    }
}
