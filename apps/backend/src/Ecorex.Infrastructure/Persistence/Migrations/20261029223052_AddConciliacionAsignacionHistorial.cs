using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConciliacionAsignacionHistorial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "rechazado",
                table: "conciliacion_dian_renglones",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "conciliacion_dian_asignaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    renglon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_to_tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    accion = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion_dian_asignaciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_conciliacion_dian_asignaciones_conciliacion_dian_renglones_",
                        column: x => x.renglon_id,
                        principalTable: "conciliacion_dian_renglones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_dian_asignaciones_renglon_id",
                table: "conciliacion_dian_asignaciones",
                column: "renglon_id");

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_dian_asignaciones_tenant_id_renglon_id",
                table: "conciliacion_dian_asignaciones",
                columns: new[] { "tenant_id", "renglon_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conciliacion_dian_asignaciones");

            migrationBuilder.DropColumn(
                name: "rechazado",
                table: "conciliacion_dian_renglones");
        }
    }
}
