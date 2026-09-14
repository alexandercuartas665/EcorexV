using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskItemConversationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "conversation_id",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_items_tenant_id_conversation_id",
                table: "task_items",
                columns: new[] { "tenant_id", "conversation_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_task_items_tenant_id_conversation_id",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "conversation_id",
                table: "task_items");
        }
    }
}
