using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulerRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_scheduled_job_runs_tenant_id_job_id_rule_id_fired_at",
                table: "scheduled_job_runs");

            migrationBuilder.AddColumn<int>(
                name: "attempt",
                table: "scheduled_job_runs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "attempt",
                table: "scheduled_job_rules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pending_window_at",
                table: "scheduled_job_rules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_job_runs_tenant_id_job_id_rule_id_fired_at_attempt",
                table: "scheduled_job_runs",
                columns: new[] { "tenant_id", "job_id", "rule_id", "fired_at", "attempt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_scheduled_job_runs_tenant_id_job_id_rule_id_fired_at_attempt",
                table: "scheduled_job_runs");

            migrationBuilder.DropColumn(
                name: "attempt",
                table: "scheduled_job_runs");

            migrationBuilder.DropColumn(
                name: "attempt",
                table: "scheduled_job_rules");

            migrationBuilder.DropColumn(
                name: "pending_window_at",
                table: "scheduled_job_rules");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_job_runs_tenant_id_job_id_rule_id_fired_at",
                table: "scheduled_job_runs",
                columns: new[] { "tenant_id", "job_id", "rule_id", "fired_at" },
                unique: true);
        }
    }
}
