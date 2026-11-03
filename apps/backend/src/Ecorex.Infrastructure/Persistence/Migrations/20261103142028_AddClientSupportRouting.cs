using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientSupportRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "support_status",
                table: "messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // ADR-0125 v2: las filas existentes quedan "None" (no "": el conversor enum<->string haria
                // fallar la lectura de mensajes viejos al materializar un valor vacio).
                defaultValue: "None");

            migrationBuilder.AddColumn<Guid>(
                name: "support_task_item_id",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "current_task_item_id",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_id_support_status",
                table: "messages",
                columns: new[] { "tenant_id", "support_status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_messages_tenant_id_support_status",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "support_status",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "support_task_item_id",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "current_task_item_id",
                table: "conversations");
        }
    }
}
