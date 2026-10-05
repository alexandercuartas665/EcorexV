using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowNodeRuntimeLayoutOffset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "runtime_layout_dx",
                table: "workflow_nodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "runtime_layout_dy",
                table: "workflow_nodes",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "runtime_layout_dx",
                table: "workflow_nodes");

            migrationBuilder.DropColumn(
                name: "runtime_layout_dy",
                table: "workflow_nodes");
        }
    }
}
