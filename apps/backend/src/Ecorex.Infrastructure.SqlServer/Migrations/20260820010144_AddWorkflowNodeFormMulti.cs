using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowNodeFormMulti : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_workflow_node_forms_node_id",
                table: "workflow_node_forms");

            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "workflow_node_forms",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_forms_node_id_definition_id",
                table: "workflow_node_forms",
                columns: new[] { "node_id", "definition_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_workflow_node_forms_node_id_definition_id",
                table: "workflow_node_forms");

            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "workflow_node_forms");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_forms_node_id",
                table: "workflow_node_forms",
                column: "node_id",
                unique: true);
        }
    }
}
