using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddScraping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scrape_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    selector = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_result_summary = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scrape_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scrape_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    item_count = table.Column<int>(type: "int", nullable: false),
                    duration_ms = table.Column<int>(type: "int", nullable: false),
                    error_message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    result_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scrape_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_scrape_runs_scrape_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "scrape_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scrape_runs_source_id",
                table: "scrape_runs",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_scrape_runs_tenant_id_source_id_created_at",
                table: "scrape_runs",
                columns: new[] { "tenant_id", "source_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_scrape_sources_tenant_id_name",
                table: "scrape_sources",
                columns: new[] { "tenant_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scrape_runs");

            migrationBuilder.DropTable(
                name: "scrape_sources");
        }
    }
}
