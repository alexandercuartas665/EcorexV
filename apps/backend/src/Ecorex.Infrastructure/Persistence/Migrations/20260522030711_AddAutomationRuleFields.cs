using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationRuleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "threshold_hours",
                table: "automation_rules",
                newName: "threshold_minutes");

            migrationBuilder.AddColumn<int>(
                name: "execution_count",
                table: "automation_rules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "shift_name",
                table: "automation_rules",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "template_category",
                table: "automation_rules",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_window_end",
                table: "automation_rules",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_window_start",
                table: "automation_rules",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "execution_count",
                table: "automation_rules");

            migrationBuilder.DropColumn(
                name: "shift_name",
                table: "automation_rules");

            migrationBuilder.DropColumn(
                name: "template_category",
                table: "automation_rules");

            migrationBuilder.DropColumn(
                name: "time_window_end",
                table: "automation_rules");

            migrationBuilder.DropColumn(
                name: "time_window_start",
                table: "automation_rules");

            migrationBuilder.RenameColumn(
                name: "threshold_minutes",
                table: "automation_rules",
                newName: "threshold_hours");
        }
    }
}
