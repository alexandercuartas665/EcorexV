using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceRetryPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "voice_call_count",
                table: "workflow_step_histories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "call_window_end_hour",
                table: "workflow_node_agents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "call_window_start_hour",
                table: "workflow_node_agents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_voice_calls",
                table: "workflow_node_agents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "voice_retry_minutes",
                table: "workflow_node_agents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "disconnection_reason",
                table: "voice_calls",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "voice_call_count",
                table: "workflow_step_histories");

            migrationBuilder.DropColumn(
                name: "call_window_end_hour",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "call_window_start_hour",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "max_voice_calls",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "voice_retry_minutes",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "disconnection_reason",
                table: "voice_calls");
        }
    }
}
