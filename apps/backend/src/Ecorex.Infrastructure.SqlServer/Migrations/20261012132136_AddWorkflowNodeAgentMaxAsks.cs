using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowNodeAgentMaxAsks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "agent_max_asks",
                table: "workflow_nodes",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agent_max_asks",
                table: "workflow_nodes");
        }
    }
}
