using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactWorkflowRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_workflow_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_workflow_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_workflow_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tercero_id = table.Column<Guid>(type: "uuid", nullable: false),
                    window_date = table.Column<DateOnly>(type: "date", nullable: false),
                    dispatched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    external_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    error = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_workflow_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_workflow_runs_contact_workflow_steps_contact_workfl",
                        column: x => x.contact_workflow_step_id,
                        principalTable: "contact_workflow_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contact_workflow_runs_contact_workflow_step_id",
                table: "contact_workflow_runs",
                column: "contact_workflow_step_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_workflow_runs_tenant_id_contact_workflow_schedule_i",
                table: "contact_workflow_runs",
                columns: new[] { "tenant_id", "contact_workflow_schedule_id", "window_date" });

            migrationBuilder.CreateIndex(
                name: "ix_contact_workflow_runs_tenant_id_contact_workflow_step_id_co",
                table: "contact_workflow_runs",
                columns: new[] { "tenant_id", "contact_workflow_step_id", "contact_workflow_schedule_id", "tercero_id", "window_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_workflow_runs");
        }
    }
}
