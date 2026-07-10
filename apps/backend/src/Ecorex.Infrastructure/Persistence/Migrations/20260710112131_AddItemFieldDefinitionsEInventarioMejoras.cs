using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddItemFieldDefinitionsEInventarioMejoras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "datos_tienda_json",
                table: "items",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "es_principal",
                table: "item_images",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "texto",
                table: "item_images",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "item_field_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    field_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    options = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    column = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_field_definitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_field_definitions_item_types_item_type_id",
                        column: x => x.item_type_id,
                        principalTable: "item_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_item_field_definitions_item_type_id",
                table: "item_field_definitions",
                column: "item_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_field_definitions_tenant_id_item_type_id_field_key",
                table: "item_field_definitions",
                columns: new[] { "tenant_id", "item_type_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_field_definitions_tenant_id_item_type_id_sort_order",
                table: "item_field_definitions",
                columns: new[] { "tenant_id", "item_type_id", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_field_definitions");

            migrationBuilder.DropColumn(
                name: "datos_tienda_json",
                table: "items");

            migrationBuilder.DropColumn(
                name: "es_principal",
                table: "item_images");

            migrationBuilder.DropColumn(
                name: "texto",
                table: "item_images");
        }
    }
}
