using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCustomFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_fields_json",
                table: "task_items",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "task_field_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    board_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    field_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    options = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    column = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    allow_multiple = table.Column<bool>(type: "boolean", nullable: false),
                    formula = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    repeat_with_field_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_field_definitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_field_definitions_task_boards_board_id",
                        column: x => x.board_id,
                        principalTable: "task_boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_field_definitions_board_id",
                table: "task_field_definitions",
                column: "board_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_field_definitions_tenant_id_board_id_field_key",
                table: "task_field_definitions",
                columns: new[] { "tenant_id", "board_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_field_definitions_tenant_id_board_id_sort_order",
                table: "task_field_definitions",
                columns: new[] { "tenant_id", "board_id", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_field_definitions");

            migrationBuilder.DropColumn(
                name: "custom_fields_json",
                table: "task_items");
        }
    }
}
