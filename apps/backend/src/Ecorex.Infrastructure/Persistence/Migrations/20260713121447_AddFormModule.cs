using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "filter_fields_json",
                table: "form_definitions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_module",
                table: "form_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "list_columns_json",
                table: "form_definitions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "module_icon",
                table: "form_definitions",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "module_menu_node_id",
                table: "form_definitions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "filter_fields_json",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "is_module",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "list_columns_json",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "module_icon",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "module_menu_node_id",
                table: "form_definitions");
        }
    }
}
