using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTerceroNotaConceptoLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "concepto_actividad_id",
                table: "tercero_notas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "form_response_id",
                table: "tercero_notas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "valor",
                table: "tercero_notas",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tercero_notas_concepto_actividad_id",
                table: "tercero_notas",
                column: "concepto_actividad_id");

            migrationBuilder.CreateIndex(
                name: "ix_tercero_notas_form_response_id",
                table: "tercero_notas",
                column: "form_response_id");

            migrationBuilder.AddForeignKey(
                name: "fk_tercero_notas_conceptos_actividad_concepto_actividad_id",
                table: "tercero_notas",
                column: "concepto_actividad_id",
                principalTable: "conceptos_actividad",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tercero_notas_form_responses_form_response_id",
                table: "tercero_notas",
                column: "form_response_id",
                principalTable: "form_responses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tercero_notas_conceptos_actividad_concepto_actividad_id",
                table: "tercero_notas");

            migrationBuilder.DropForeignKey(
                name: "fk_tercero_notas_form_responses_form_response_id",
                table: "tercero_notas");

            migrationBuilder.DropIndex(
                name: "ix_tercero_notas_concepto_actividad_id",
                table: "tercero_notas");

            migrationBuilder.DropIndex(
                name: "ix_tercero_notas_form_response_id",
                table: "tercero_notas");

            migrationBuilder.DropColumn(
                name: "concepto_actividad_id",
                table: "tercero_notas");

            migrationBuilder.DropColumn(
                name: "form_response_id",
                table: "tercero_notas");

            migrationBuilder.DropColumn(
                name: "valor",
                table: "tercero_notas");
        }
    }
}
