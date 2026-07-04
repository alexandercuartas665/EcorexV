using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityBoards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "board_id",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "board_sort_order",
                table: "task_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "column_id",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "start_date",
                table: "task_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "task_boards",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "due_date",
                table: "task_boards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "task_boards",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "task_boards",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "task_item_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_item_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_item_assignments_task_items_task_item_id",
                        column: x => x.task_item_id,
                        principalTable: "task_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_item_assignments_tenant_users_tenant_user_id",
                        column: x => x.tenant_user_id,
                        principalTable: "tenant_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_item_checklist_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by_tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_item_checklist_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_item_checklist_items_task_items_task_item_id",
                        column: x => x.task_item_id,
                        principalTable: "task_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_items_board_id",
                table: "task_items",
                column: "board_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_items_column_id",
                table: "task_items",
                column: "column_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_items_tenant_id_board_id_column_id_board_sort_order",
                table: "task_items",
                columns: new[] { "tenant_id", "board_id", "column_id", "board_sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_task_boards_tenant_id_code",
                table: "task_boards",
                columns: new[] { "tenant_id", "code" },
                unique: true,
                filter: "code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_task_boards_tenant_id_kind_is_archived",
                table: "task_boards",
                columns: new[] { "tenant_id", "kind", "is_archived" });

            migrationBuilder.CreateIndex(
                name: "ix_task_item_assignments_task_item_id_tenant_user_id",
                table: "task_item_assignments",
                columns: new[] { "task_item_id", "tenant_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_item_assignments_tenant_id_tenant_user_id",
                table: "task_item_assignments",
                columns: new[] { "tenant_id", "tenant_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_item_assignments_tenant_user_id",
                table: "task_item_assignments",
                column: "tenant_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_item_checklist_items_task_item_id_sort_order",
                table: "task_item_checklist_items",
                columns: new[] { "task_item_id", "sort_order" });

            migrationBuilder.AddForeignKey(
                name: "fk_task_items_task_board_columns_column_id",
                table: "task_items",
                column: "column_id",
                principalTable: "task_board_columns",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_task_items_task_boards_board_id",
                table: "task_items",
                column: "board_id",
                principalTable: "task_boards",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_task_items_task_board_columns_column_id",
                table: "task_items");

            migrationBuilder.DropForeignKey(
                name: "fk_task_items_task_boards_board_id",
                table: "task_items");

            migrationBuilder.DropTable(
                name: "task_item_assignments");

            migrationBuilder.DropTable(
                name: "task_item_checklist_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_board_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_column_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_tenant_id_board_id_column_id_board_sort_order",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_boards_tenant_id_code",
                table: "task_boards");

            migrationBuilder.DropIndex(
                name: "ix_task_boards_tenant_id_kind_is_archived",
                table: "task_boards");

            migrationBuilder.DropColumn(
                name: "board_id",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "board_sort_order",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "column_id",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "code",
                table: "task_boards");

            migrationBuilder.DropColumn(
                name: "due_date",
                table: "task_boards");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "task_boards");

            migrationBuilder.DropColumn(
                name: "status",
                table: "task_boards");
        }
    }
}
