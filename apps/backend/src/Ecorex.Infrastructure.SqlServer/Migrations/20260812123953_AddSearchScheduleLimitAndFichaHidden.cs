using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchScheduleLimitAndFichaHidden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_hidden",
                table: "tercero_ficha_definitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_run_at",
                table: "contact_search_definitions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_contacts",
                table: "contact_search_definitions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "schedule",
                table: "contact_search_definitions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_hidden",
                table: "tercero_ficha_definitions");

            migrationBuilder.DropColumn(
                name: "last_run_at",
                table: "contact_search_definitions");

            migrationBuilder.DropColumn(
                name: "max_contacts",
                table: "contact_search_definitions");

            migrationBuilder.DropColumn(
                name: "schedule",
                table: "contact_search_definitions");
        }
    }
}
