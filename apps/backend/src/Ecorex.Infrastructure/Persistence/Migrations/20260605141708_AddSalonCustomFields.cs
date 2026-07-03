using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalonCustomFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "field_values_json",
                table: "clients",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "field_values_json",
                table: "appointments",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "salon_field_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    field_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    field_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    options = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    column = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    show_on_board = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salon_field_definitions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_salon_field_definitions_tenant_id_scope_field_key",
                table: "salon_field_definitions",
                columns: new[] { "tenant_id", "scope", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_salon_field_definitions_tenant_id_scope_sort_order",
                table: "salon_field_definitions",
                columns: new[] { "tenant_id", "scope", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "salon_field_definitions");

            migrationBuilder.DropColumn(
                name: "field_values_json",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "field_values_json",
                table: "appointments");
        }
    }
}
