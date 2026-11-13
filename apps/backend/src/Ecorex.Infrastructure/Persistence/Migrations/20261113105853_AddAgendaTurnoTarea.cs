using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgendaTurnoTarea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "conversation_id",
                table: "agenda_turnos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "task_item_id",
                table: "agenda_turnos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "concepto_id",
                table: "agenda_servicios",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_agenda_turnos_task_item_id",
                table: "agenda_turnos",
                column: "task_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_turnos_tenant_id_conversation_id",
                table: "agenda_turnos",
                columns: new[] { "tenant_id", "conversation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_turnos_tenant_id_tercero_id",
                table: "agenda_turnos",
                columns: new[] { "tenant_id", "tercero_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_servicios_concepto_id",
                table: "agenda_servicios",
                column: "concepto_id");

            migrationBuilder.AddForeignKey(
                name: "fk_agenda_servicios_actividad_subcategorias_concepto_id",
                table: "agenda_servicios",
                column: "concepto_id",
                principalTable: "actividad_subcategorias",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_agenda_turnos_task_items_task_item_id",
                table: "agenda_turnos",
                column: "task_item_id",
                principalTable: "task_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_agenda_servicios_actividad_subcategorias_concepto_id",
                table: "agenda_servicios");

            migrationBuilder.DropForeignKey(
                name: "fk_agenda_turnos_task_items_task_item_id",
                table: "agenda_turnos");

            migrationBuilder.DropIndex(
                name: "ix_agenda_turnos_task_item_id",
                table: "agenda_turnos");

            migrationBuilder.DropIndex(
                name: "ix_agenda_turnos_tenant_id_conversation_id",
                table: "agenda_turnos");

            migrationBuilder.DropIndex(
                name: "ix_agenda_turnos_tenant_id_tercero_id",
                table: "agenda_turnos");

            migrationBuilder.DropIndex(
                name: "ix_agenda_servicios_concepto_id",
                table: "agenda_servicios");

            migrationBuilder.DropColumn(
                name: "conversation_id",
                table: "agenda_turnos");

            migrationBuilder.DropColumn(
                name: "task_item_id",
                table: "agenda_turnos");

            migrationBuilder.DropColumn(
                name: "concepto_id",
                table: "agenda_servicios");
        }
    }
}
