using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
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
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agent_run_log",
                table: "workflow_step_histories",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "agent_tokens_used",
                table: "workflow_step_histories",
                type: "int",
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
