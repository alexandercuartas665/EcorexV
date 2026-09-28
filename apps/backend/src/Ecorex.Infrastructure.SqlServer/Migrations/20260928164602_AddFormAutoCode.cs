using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddFormAutoCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_code_enabled",
                table: "form_definitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "auto_code_pad_width",
                table: "form_definitions",
                type: "int",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<string>(
                name: "auto_code_target_field_code",
                table: "form_definitions",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_code_enabled",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "auto_code_pad_width",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "auto_code_target_field_code",
                table: "form_definitions");
        }
    }
}
