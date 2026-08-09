using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskSubtasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "parent_id",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_items_parent_id",
                table: "task_items",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_items_tenant_id_parent_id",
                table: "task_items",
                columns: new[] { "tenant_id", "parent_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_task_items_task_items_parent_id",
                table: "task_items",
                column: "parent_id",
                principalTable: "task_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_task_items_task_items_parent_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_parent_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_tenant_id_parent_id",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "parent_id",
                table: "task_items");
        }
    }
}
