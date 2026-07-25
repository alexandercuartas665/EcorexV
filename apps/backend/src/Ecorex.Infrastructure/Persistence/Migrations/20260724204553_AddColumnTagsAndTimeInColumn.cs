using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddColumnTagsAndTimeInColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "column_entered_at",
                table: "task_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "task_board_column_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    column_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_board_column_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_board_column_tags_task_board_columns_column_id",
                        column: x => x.column_id,
                        principalTable: "task_board_columns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_board_column_tags_task_item_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "task_item_tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_board_column_tags_column_id_tag_id",
                table: "task_board_column_tags",
                columns: new[] { "column_id", "tag_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_board_column_tags_tag_id",
                table: "task_board_column_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_board_column_tags_tenant_id_column_id",
                table: "task_board_column_tags",
                columns: new[] { "tenant_id", "column_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_board_column_tags");

            migrationBuilder.DropColumn(
                name: "column_entered_at",
                table: "task_items");
        }
    }
}
