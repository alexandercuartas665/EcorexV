using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskItemTercero : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tercero_id",
                table: "task_items",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_items_tenant_id_tercero_id",
                table: "task_items",
                columns: new[] { "tenant_id", "tercero_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_items_tercero_id",
                table: "task_items",
                column: "tercero_id");

            migrationBuilder.AddForeignKey(
                name: "fk_task_items_terceros_tercero_id",
                table: "task_items",
                column: "tercero_id",
                principalTable: "terceros",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_task_items_terceros_tercero_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_tenant_id_tercero_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_tercero_id",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "tercero_id",
                table: "task_items");
        }
    }
}
