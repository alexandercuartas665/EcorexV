using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskItemSourceTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_task_id",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_items_source_task_id",
                table: "task_items",
                column: "source_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_items_tenant_id_source_task_id",
                table: "task_items",
                columns: new[] { "tenant_id", "source_task_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_task_items_task_items_source_task_id",
                table: "task_items",
                column: "source_task_id",
                principalTable: "task_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_task_items_task_items_source_task_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_source_task_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_tenant_id_source_task_id",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "source_task_id",
                table: "task_items");
        }
    }
}
