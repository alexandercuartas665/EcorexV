using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
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
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "conciliacion_dian_asignaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    renglon_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assigned_to_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    task_number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    accion = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion_dian_asignaciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_conciliacion_dian_asignaciones_conciliacion_dian_renglones_renglon_id",
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
