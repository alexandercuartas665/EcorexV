using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scheduled_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    priority = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    area_entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    category_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    subcategory_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_job_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    job_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_job_channels", x => x.id);
                    table.ForeignKey(
                        name: "fk_scheduled_job_channels_scheduled_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "scheduled_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_job_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    job_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    frequency = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    interval_num = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    weekdays = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    month_ordinal = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    month_weekday = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    day_of_month = table.Column<int>(type: "int", nullable: true),
                    at_time = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    repeat_intraday = table.Column<bool>(type: "bit", nullable: false),
                    repeat_every_hours = table.Column<int>(type: "int", nullable: true),
                    repeat_from = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    repeat_to = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    next_run_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_job_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_scheduled_job_rules_scheduled_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "scheduled_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_job_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    job_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    fired_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    result = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    detail = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    created_entity_ref = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_job_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_scheduled_job_runs_scheduled_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "scheduled_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_job_channels_job_id_channel",
                table: "scheduled_job_channels",
                columns: new[] { "job_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_job_rules_job_id_sort_order",
                table: "scheduled_job_rules",
                columns: new[] { "job_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_job_rules_next_run_at",
                table: "scheduled_job_rules",
                column: "next_run_at");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_job_runs_job_id",
                table: "scheduled_job_runs",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_job_runs_tenant_id_job_id_fired_at",
                table: "scheduled_job_runs",
                columns: new[] { "tenant_id", "job_id", "fired_at" });

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_jobs_tenant_id_code",
                table: "scheduled_jobs",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_jobs_tenant_id_status",
                table: "scheduled_jobs",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduled_job_channels");

            migrationBuilder.DropTable(
                name: "scheduled_job_rules");

            migrationBuilder.DropTable(
                name: "scheduled_job_runs");

            migrationBuilder.DropTable(
                name: "scheduled_jobs");
        }
    }
}
