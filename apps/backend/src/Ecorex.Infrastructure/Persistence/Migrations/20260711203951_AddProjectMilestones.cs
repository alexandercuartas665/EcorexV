using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectMilestones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "milestone_id",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "project_milestones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_milestones", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_milestones_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_items_milestone_id",
                table: "task_items",
                column: "milestone_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_items_tenant_id_milestone_id",
                table: "task_items",
                columns: new[] { "tenant_id", "milestone_id" });

            migrationBuilder.CreateIndex(
                name: "ix_project_milestones_project_id_sort_order",
                table: "project_milestones",
                columns: new[] { "project_id", "sort_order" });

            migrationBuilder.AddForeignKey(
                name: "fk_task_items_project_milestones_milestone_id",
                table: "task_items",
                column: "milestone_id",
                principalTable: "project_milestones",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_task_items_project_milestones_milestone_id",
                table: "task_items");

            migrationBuilder.DropTable(
                name: "project_milestones");

            migrationBuilder.DropIndex(
                name: "ix_task_items_milestone_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_tenant_id_milestone_id",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "milestone_id",
                table: "task_items");
        }
    }
}
