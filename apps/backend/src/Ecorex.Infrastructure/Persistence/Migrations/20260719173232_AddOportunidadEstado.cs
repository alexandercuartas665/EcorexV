using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOportunidadEstado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "estado_id",
                table: "oportunidades",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "oportunidad_estados",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    color = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oportunidad_estados", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_oportunidades_estado_id",
                table: "oportunidades",
                column: "estado_id");

            migrationBuilder.CreateIndex(
                name: "ix_oportunidades_tenant_id_estado_id_sort_order",
                table: "oportunidades",
                columns: new[] { "tenant_id", "estado_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_oportunidad_estados_tenant_id_sort_order",
                table: "oportunidad_estados",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.AddForeignKey(
                name: "fk_oportunidades_oportunidad_estados_estado_id",
                table: "oportunidades",
                column: "estado_id",
                principalTable: "oportunidad_estados",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_oportunidades_oportunidad_estados_estado_id",
                table: "oportunidades");

            migrationBuilder.DropTable(
                name: "oportunidad_estados");

            migrationBuilder.DropIndex(
                name: "ix_oportunidades_estado_id",
                table: "oportunidades");

            migrationBuilder.DropIndex(
                name: "ix_oportunidades_tenant_id_estado_id_sort_order",
                table: "oportunidades");

            migrationBuilder.DropColumn(
                name: "estado_id",
                table: "oportunidades");
        }
    }
}
