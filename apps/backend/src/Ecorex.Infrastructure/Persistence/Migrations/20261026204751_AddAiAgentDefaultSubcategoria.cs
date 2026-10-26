using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAgentDefaultSubcategoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "default_subcategoria_id",
                table: "ai_agents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_agents_default_subcategoria_id",
                table: "ai_agents",
                column: "default_subcategoria_id");

            migrationBuilder.AddForeignKey(
                name: "fk_ai_agents_actividad_subcategorias_default_subcategoria_id",
                table: "ai_agents",
                column: "default_subcategoria_id",
                principalTable: "actividad_subcategorias",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ai_agents_actividad_subcategorias_default_subcategoria_id",
                table: "ai_agents");

            migrationBuilder.DropIndex(
                name: "ix_ai_agents_default_subcategoria_id",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "default_subcategoria_id",
                table: "ai_agents");
        }
    }
}
