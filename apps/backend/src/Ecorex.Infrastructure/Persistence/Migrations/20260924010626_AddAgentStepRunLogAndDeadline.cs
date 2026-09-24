using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentStepRunLogAndDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "agent_deadline_at",
                table: "workflow_step_histories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent_run_log",
                table: "workflow_step_histories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "agent_tokens_used",
                table: "workflow_step_histories",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agent_deadline_at",
                table: "workflow_step_histories");

            migrationBuilder.DropColumn(
                name: "agent_run_log",
                table: "workflow_step_histories");

            migrationBuilder.DropColumn(
                name: "agent_tokens_used",
                table: "workflow_step_histories");
        }
    }
}
