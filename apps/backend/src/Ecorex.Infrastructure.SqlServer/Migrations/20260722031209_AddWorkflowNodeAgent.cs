using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowNodeAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_node_agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    node_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ai_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    autonomy = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_node_agents", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_node_agents_ai_agents_ai_agent_id",
                        column: x => x.ai_agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_node_agents_workflow_nodes_node_id",
                        column: x => x.node_id,
                        principalTable: "workflow_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_agents_ai_agent_id",
                table: "workflow_node_agents",
                column: "ai_agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_agents_node_id",
                table: "workflow_node_agents",
                column: "node_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_agents_tenant_id_node_id",
                table: "workflow_node_agents",
                columns: new[] { "tenant_id", "node_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_node_agents");
        }
    }
}
