using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddImportProcessRunMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "key_column",
                table: "import_processes",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mode",
                table: "import_processes",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Replace");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "key_column",
                table: "import_processes");

            migrationBuilder.DropColumn(
                name: "mode",
                table: "import_processes");
        }
    }
}
