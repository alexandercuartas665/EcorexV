using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddReportDefinitionRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "report_definition_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    report_definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rol_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_definition_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_definition_roles_report_definitions_report_definition_id",
                        column: x => x.report_definition_id,
                        principalTable: "report_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_report_definition_roles_roles_rol_id",
                        column: x => x.rol_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_report_definition_roles_report_definition_id",
                table: "report_definition_roles",
                column: "report_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_definition_roles_rol_id",
                table: "report_definition_roles",
                column: "rol_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_definition_roles_tenant_id_report_definition_id_rol_id",
                table: "report_definition_roles",
                columns: new[] { "tenant_id", "report_definition_id", "rol_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_definition_roles_tenant_id_rol_id",
                table: "report_definition_roles",
                columns: new[] { "tenant_id", "rol_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "report_definition_roles");
        }
    }
}
