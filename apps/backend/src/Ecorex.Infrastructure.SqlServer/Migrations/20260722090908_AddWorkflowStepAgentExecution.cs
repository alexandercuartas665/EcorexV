using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowStepAgentExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "agent_attempted_at",
                table: "workflow_step_histories",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent_failure_reason",
                table: "workflow_step_histories",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent_proposal_comment",
                table: "workflow_step_histories",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent_proposal_result",
                table: "workflow_step_histories",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "executed_by_ai_agent_id",
                table: "workflow_step_histories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_histories_is_current_agent_attempted_at",
                table: "workflow_step_histories",
                columns: new[] { "is_current", "agent_attempted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_workflow_step_histories_is_current_agent_attempted_at",
                table: "workflow_step_histories");

            migrationBuilder.DropColumn(
                name: "agent_attempted_at",
                table: "workflow_step_histories");

            migrationBuilder.DropColumn(
                name: "agent_failure_reason",
                table: "workflow_step_histories");

            migrationBuilder.DropColumn(
                name: "agent_proposal_comment",
                table: "workflow_step_histories");

            migrationBuilder.DropColumn(
                name: "agent_proposal_result",
                table: "workflow_step_histories");

            migrationBuilder.DropColumn(
                name: "executed_by_ai_agent_id",
                table: "workflow_step_histories");
        }
    }
}
