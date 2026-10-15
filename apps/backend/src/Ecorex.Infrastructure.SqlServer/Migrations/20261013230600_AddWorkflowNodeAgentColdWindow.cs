using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowNodeAgentColdWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cold_window_minutes",
                table: "workflow_node_agents",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cold_window_minutes",
                table: "workflow_node_agents");
        }
    }
}
