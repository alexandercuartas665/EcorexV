using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "disabled_reason",
                table: "import_processes",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_run_at",
                table: "import_processes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "import_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    trigger = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    result = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    inserted = table.Column<int>(type: "integer", nullable: false),
                    updated = table.Column<int>(type: "integer", nullable: false),
                    deleted = table.Column<int>(type: "integer", nullable: false),
                    detail = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_runs_import_processes_process_id",
                        column: x => x.process_id,
                        principalTable: "import_processes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_processes_next_run_at",
                table: "import_processes",
                column: "next_run_at");

            migrationBuilder.CreateIndex(
                name: "ix_import_runs_process_id",
                table: "import_runs",
                column: "process_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_runs_tenant_id_correlation_id",
                table: "import_runs",
                columns: new[] { "tenant_id", "correlation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_import_runs_tenant_id_process_id_fired_at",
                table: "import_runs",
                columns: new[] { "tenant_id", "process_id", "fired_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_runs");

            migrationBuilder.DropIndex(
                name: "ix_import_processes_next_run_at",
                table: "import_processes");

            migrationBuilder.DropColumn(
                name: "disabled_reason",
                table: "import_processes");

            migrationBuilder.DropColumn(
                name: "next_run_at",
                table: "import_processes");
        }
    }
}
