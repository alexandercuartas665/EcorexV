using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
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
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "failure_retries",
                table: "workflow_node_agents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "failure_route",
                table: "workflow_node_agents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "on_failure",
                table: "workflow_node_agents",
                type: "integer",
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
