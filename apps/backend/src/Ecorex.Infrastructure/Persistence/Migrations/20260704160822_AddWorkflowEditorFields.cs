using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowEditorFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "h",
                table: "workflow_nodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "w",
                table: "workflow_nodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "x",
                table: "workflow_nodes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "y",
                table: "workflow_nodes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "workflow_definitions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_paused",
                table: "workflow_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "h",
                table: "workflow_nodes");

            migrationBuilder.DropColumn(
                name: "w",
                table: "workflow_nodes");

            migrationBuilder.DropColumn(
                name: "x",
                table: "workflow_nodes");

            migrationBuilder.DropColumn(
                name: "y",
                table: "workflow_nodes");

            migrationBuilder.DropColumn(
                name: "category",
                table: "workflow_definitions");

            migrationBuilder.DropColumn(
                name: "is_paused",
                table: "workflow_definitions");
        }
    }
}
