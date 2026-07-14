using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    priority = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    area_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subcategory_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_job_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    frequency = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    interval_num = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    weekdays = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    month_ordinal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    month_weekday = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    day_of_month = table.Column<int>(type: "integer", nullable: true),
                    at_time = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    repeat_intraday = table.Column<bool>(type: "boolean", nullable: false),
                    repeat_every_hours = table.Column<int>(type: "integer", nullable: true),
                    repeat_from = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    repeat_to = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    result = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    detail = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    created_entity_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
