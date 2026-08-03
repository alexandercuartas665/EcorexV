using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddReportTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "template_id",
                table: "report_definitions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "report_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    source_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    spec_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    rdl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    required_source_kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    required_container_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    category = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    icon = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    is_published = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_report_definitions_tenant_id_template_id",
                table: "report_definitions",
                columns: new[] { "tenant_id", "template_id" });

            migrationBuilder.CreateIndex(
                name: "ix_report_templates_is_published",
                table: "report_templates",
                column: "is_published");

            migrationBuilder.CreateIndex(
                name: "ix_report_templates_source_key",
                table: "report_templates",
                column: "source_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "report_templates");

            migrationBuilder.DropIndex(
                name: "ix_report_definitions_tenant_id_template_id",
                table: "report_definitions");

            migrationBuilder.DropColumn(
                name: "template_id",
                table: "report_definitions");
        }
    }
}
