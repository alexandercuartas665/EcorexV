using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowNodeNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_node_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instance_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    node_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    author_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    author_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_node_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_node_notes_workflow_instances_instance_id",
                        column: x => x.instance_id,
                        principalTable: "workflow_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_workflow_node_notes_workflow_nodes_node_id",
                        column: x => x.node_id,
                        principalTable: "workflow_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_notes_instance_id_node_id",
                table: "workflow_node_notes",
                columns: new[] { "instance_id", "node_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_notes_node_id",
                table: "workflow_node_notes",
                column: "node_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_node_notes");
        }
    }
}
