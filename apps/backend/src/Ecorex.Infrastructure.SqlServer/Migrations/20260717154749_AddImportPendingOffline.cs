using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddImportPendingOffline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pending_since",
                table: "import_processes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_processes_pending_since",
                table: "import_processes",
                column: "pending_since");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_import_processes_pending_since",
                table: "import_processes");

            migrationBuilder.DropColumn(
                name: "pending_since",
                table: "import_processes");
        }
    }
}
