using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentColdTemplateVarsAndHeader : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "whats_app_template_header_media_url",
                table: "workflow_node_agents",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "whats_app_template_vars_json",
                table: "workflow_node_agents",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "whats_app_template_header_media_url",
                table: "workflow_node_agents");

            migrationBuilder.DropColumn(
                name: "whats_app_template_vars_json",
                table: "workflow_node_agents");
        }
    }
}
