using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddProspectoEmpresaFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "empresa_prospecto_id",
                table: "prospectos_scrapeados",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_prospectos_scrapeados_empresa_prospecto_id",
                table: "prospectos_scrapeados",
                column: "empresa_prospecto_id");

            migrationBuilder.AddForeignKey(
                name: "fk_prospectos_scrapeados_prospectos_scrapeados_empresa_prospecto_id",
                table: "prospectos_scrapeados",
                column: "empresa_prospecto_id",
                principalTable: "prospectos_scrapeados",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_prospectos_scrapeados_prospectos_scrapeados_empresa_prospecto_id",
                table: "prospectos_scrapeados");

            migrationBuilder.DropIndex(
                name: "ix_prospectos_scrapeados_empresa_prospecto_id",
                table: "prospectos_scrapeados");

            migrationBuilder.DropColumn(
                name: "empresa_prospecto_id",
                table: "prospectos_scrapeados");
        }
    }
}
