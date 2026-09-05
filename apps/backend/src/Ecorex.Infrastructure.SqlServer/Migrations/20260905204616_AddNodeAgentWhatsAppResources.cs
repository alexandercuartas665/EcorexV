using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeAgentWhatsAppResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "pending_whats_app_conversation_id",
                table: "workflow_step_histories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "whats_app_line_id",
                table: "workflow_node_agents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "whats_app_template_lang",
                table: "workflow_node_agents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "whats_app_template_name",
                table: "workflow_node_agents",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_histories_pending_whats_app_conversation_id",
                table: "workflow_step_histories",
                column: "pending_whats_app_conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_agents_whats_app_line_id",
                table: "workflow_node_agents",
                column: "whats_app_line_id");

            migrationBuilder.AddForeignKey(
                name: "fk_workflow_node_agents_whats_app_lines_whats_app_line_id",
                table: "workflow_node_agents",
                column: "whats_app_line_id",
                principalTable: "whats_app_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_workflow_node_agents_whats_app_lines_whats_app_line_id",
                table: "workflow_node_agents");

            migrationBuilder.DropIndex(
                name: "ix_workflow_step_histories_pending_whats_app_conversation_id",
                table: "workflow_step_histories");

            migrationBuilder.DropIndex(
                name: "ix_workflow_node_agents_whats_app_line_id",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "pending_whats_app_conversation_id",
                table: "workflow_step_histories");

            migrationBuilder.DropColumn(
                name: "whats_app_line_id",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "whats_app_template_lang",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "whats_app_template_name",
                table: "workflow_node_agents");
        }
    }
}
