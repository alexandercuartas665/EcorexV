using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddFormCalcFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "aggregate",
                table: "form_questions",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "calc_expression",
                table: "form_questions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "aggregate",
                table: "form_questions");

            migrationBuilder.DropColumn(
                name: "calc_expression",
                table: "form_questions");
        }
    }
}
