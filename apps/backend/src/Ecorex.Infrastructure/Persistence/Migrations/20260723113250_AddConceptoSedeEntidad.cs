using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConceptoSedeEntidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sedes",
                table: "actividad_subcategorias");

            migrationBuilder.CreateTable(
                name: "actividad_subcategoria_sedes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subcategoria_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actividad_subcategoria_sedes", x => x.id);
                    table.ForeignKey(
                        name: "fk_actividad_subcategoria_sedes_actividad_subcategorias_subcat",
                        column: x => x.subcategoria_id,
                        principalTable: "actividad_subcategorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_actividad_subcategoria_sedes_entidades_entidad_id",
                        column: x => x.entidad_id,
                        principalTable: "entidades",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_sedes_entidad_id",
                table: "actividad_subcategoria_sedes",
                column: "entidad_id");

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_sedes_subcategoria_id_entidad_id",
                table: "actividad_subcategoria_sedes",
                columns: new[] { "subcategoria_id", "entidad_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_sedes_tenant_id_entidad_id",
                table: "actividad_subcategoria_sedes",
                columns: new[] { "tenant_id", "entidad_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "actividad_subcategoria_sedes");

            migrationBuilder.AddColumn<string>(
                name: "sedes",
                table: "actividad_subcategorias",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }
    }
}
