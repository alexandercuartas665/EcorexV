using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormRuleParamsOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "params_override_json",
                table: "form_submit_rules",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "params_override_json",
                table: "form_field_rules",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "params_override_json",
                table: "form_submit_rules");

            migrationBuilder.DropColumn(
                name: "params_override_json",
                table: "form_field_rules");
        }
    }
}
