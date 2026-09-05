using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeAgentDataResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pending_voice_call_id",
                table: "workflow_step_histories",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "colmena_client_id",
                table: "workflow_node_agents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "colmena_session_key",
                table: "workflow_node_agents",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "voice_ai_agent_id",
                table: "workflow_node_agents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_agents_colmena_client_id",
                table: "workflow_node_agents",
                column: "colmena_client_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_agents_voice_ai_agent_id",
                table: "workflow_node_agents",
                column: "voice_ai_agent_id");

            migrationBuilder.AddForeignKey(
                name: "fk_workflow_node_agents_ai_agents_voice_ai_agent_id",
                table: "workflow_node_agents",
                column: "voice_ai_agent_id",
                principalTable: "ai_agents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_workflow_node_agents_data_clients_colmena_client_id",
                table: "workflow_node_agents",
                column: "colmena_client_id",
                principalTable: "data_clients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_workflow_node_agents_ai_agents_voice_ai_agent_id",
                table: "workflow_node_agents");

            migrationBuilder.DropForeignKey(
                name: "fk_workflow_node_agents_data_clients_colmena_client_id",
                table: "workflow_node_agents");

            migrationBuilder.DropIndex(
                name: "ix_workflow_node_agents_colmena_client_id",
                table: "workflow_node_agents");

            migrationBuilder.DropIndex(
                name: "ix_workflow_node_agents_voice_ai_agent_id",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "pending_voice_call_id",
                table: "workflow_step_histories");

            migrationBuilder.DropColumn(
                name: "colmena_client_id",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "colmena_session_key",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "voice_ai_agent_id",
                table: "workflow_node_agents");
        }
    }
}
