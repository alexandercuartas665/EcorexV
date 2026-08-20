using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowAssigneeSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assignee_form_field_code",
                table: "workflow_nodes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assignee_source",
                table: "workflow_nodes",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Policy");

            migrationBuilder.AddColumn<Guid>(
                name: "started_by_tenant_user_id",
                table: "workflow_instances",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "assignee_form_field_code",
                table: "workflow_nodes");

            migrationBuilder.DropColumn(
                name: "assignee_source",
                table: "workflow_nodes");

            migrationBuilder.DropColumn(
                name: "started_by_tenant_user_id",
                table: "workflow_instances");
        }
    }
}
