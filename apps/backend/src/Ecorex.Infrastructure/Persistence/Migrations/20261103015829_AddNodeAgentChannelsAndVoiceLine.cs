using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeAgentChannelsAndVoiceLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "allowed_channels",
                table: "workflow_node_agents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "voice_line_id",
                table: "workflow_node_agents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_agents_voice_line_id",
                table: "workflow_node_agents",
                column: "voice_line_id");

            migrationBuilder.AddForeignKey(
                name: "fk_workflow_node_agents_retell_voice_lines_voice_line_id",
                table: "workflow_node_agents",
                column: "voice_line_id",
                principalTable: "retell_voice_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_workflow_node_agents_retell_voice_lines_voice_line_id",
                table: "workflow_node_agents");

            migrationBuilder.DropIndex(
                name: "ix_workflow_node_agents_voice_line_id",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "allowed_channels",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "voice_line_id",
                table: "workflow_node_agents");
        }
    }
}
