using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddScrapeFlowRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scrape_flow_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    flow_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fired_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    trigger = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    result = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    step_count = table.Column<int>(type: "int", nullable: false),
                    inserted = table.Column<int>(type: "int", nullable: false),
                    updated = table.Column<int>(type: "int", nullable: false),
                    deleted = table.Column<int>(type: "int", nullable: false),
                    detail = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scrape_flow_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_scrape_flow_runs_scrape_flows_flow_id",
                        column: x => x.flow_id,
                        principalTable: "scrape_flows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scrape_flow_runs_flow_id",
                table: "scrape_flow_runs",
                column: "flow_id");

            migrationBuilder.CreateIndex(
                name: "ix_scrape_flow_runs_tenant_id_correlation_id",
                table: "scrape_flow_runs",
                columns: new[] { "tenant_id", "correlation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_scrape_flow_runs_tenant_id_flow_id_fired_at",
                table: "scrape_flow_runs",
                columns: new[] { "tenant_id", "flow_id", "fired_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scrape_flow_runs");
        }
    }
}
