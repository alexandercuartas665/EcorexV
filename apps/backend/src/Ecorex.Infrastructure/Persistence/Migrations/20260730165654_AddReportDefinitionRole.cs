using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_definition_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_definition_roles_report_definitions_report_definitio",
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
                name: "ix_report_definition_roles_tenant_id_report_definition_id_rol_",
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
