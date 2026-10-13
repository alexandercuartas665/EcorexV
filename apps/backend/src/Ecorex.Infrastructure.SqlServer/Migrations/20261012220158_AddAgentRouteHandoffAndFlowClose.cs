using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentRouteHandoffAndFlowClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "agent_route_handoff_json",
                table: "workflow_nodes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "flow_handled_close_at",
                table: "conversations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "flow_handled_task_id",
                table: "conversations",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "agent_route_handoff_json",
                table: "workflow_nodes");

            migrationBuilder.DropColumn(
                name: "flow_handled_close_at",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "flow_handled_task_id",
                table: "conversations");
        }
    }
}
