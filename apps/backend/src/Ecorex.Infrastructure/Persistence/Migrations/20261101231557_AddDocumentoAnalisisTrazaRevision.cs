using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentoAnalisisTrazaRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "revisado_at",
                table: "documentos_analisis_paquetes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "revisado_por",
                table: "documentos_analisis_paquetes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "revision_estado",
                table: "documentos_analisis_paquetes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "paginas_enviadas",
                table: "documentos_analisis",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prompt_enviado",
                table: "documentos_analisis",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "revisado_at",
                table: "documentos_analisis_paquetes");

            migrationBuilder.DropColumn(
                name: "revisado_por",
                table: "documentos_analisis_paquetes");

            migrationBuilder.DropColumn(
                name: "revision_estado",
                table: "documentos_analisis_paquetes");

            migrationBuilder.DropColumn(
                name: "paginas_enviadas",
                table: "documentos_analisis");

            migrationBuilder.DropColumn(
                name: "prompt_enviado",
                table: "documentos_analisis");
        }
    }
}
