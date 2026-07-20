using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConceptoActividadSubcategoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "subcategoria_id",
                table: "conceptos_actividad",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_conceptos_actividad_subcategoria_id",
                table: "conceptos_actividad",
                column: "subcategoria_id");

            migrationBuilder.AddForeignKey(
                name: "fk_conceptos_actividad_actividad_subcategorias_subcategoria_id",
                table: "conceptos_actividad",
                column: "subcategoria_id",
                principalTable: "actividad_subcategorias",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_conceptos_actividad_actividad_subcategorias_subcategoria_id",
                table: "conceptos_actividad");

            migrationBuilder.DropIndex(
                name: "ix_conceptos_actividad_subcategoria_id",
                table: "conceptos_actividad");

            migrationBuilder.DropColumn(
                name: "subcategoria_id",
                table: "conceptos_actividad");
        }
    }
}
