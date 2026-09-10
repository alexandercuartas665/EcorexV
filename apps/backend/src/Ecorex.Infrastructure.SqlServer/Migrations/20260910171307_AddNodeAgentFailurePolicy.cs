using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeAgentFailurePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "agent_attempt_count",
                table: "workflow_step_histories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "failure_retries",
                table: "workflow_node_agents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "failure_route",
                table: "workflow_node_agents",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "on_failure",
                table: "workflow_node_agents",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agent_attempt_count",
                table: "workflow_step_histories");

            migrationBuilder.DropColumn(
                name: "failure_retries",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "failure_route",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "on_failure",
                table: "workflow_node_agents");
        }
    }
}
