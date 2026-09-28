using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDecisionTokenFooterSurvey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "footer_html",
                table: "workflow_decision_tokens",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "survey_json",
                table: "workflow_decision_tokens",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "footer_html",
                table: "workflow_decision_tokens");

            migrationBuilder.DropColumn(
                name: "survey_json",
                table: "workflow_decision_tokens");
        }
    }
}
