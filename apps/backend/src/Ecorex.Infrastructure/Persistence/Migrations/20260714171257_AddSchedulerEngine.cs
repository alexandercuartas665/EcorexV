using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulerEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "time_zone_id",
                table: "tenants",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_job_runs_tenant_id_job_id_rule_id_fired_at",
                table: "scheduled_job_runs",
                columns: new[] { "tenant_id", "job_id", "rule_id", "fired_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_scheduled_job_runs_tenant_id_job_id_rule_id_fired_at",
                table: "scheduled_job_runs");

            migrationBuilder.DropColumn(
                name: "time_zone_id",
                table: "tenants");
        }
    }
}
