using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskItemConceptoBridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "activity_type_id",
                table: "task_items",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "entidad_id",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "subcategoria_id",
                table: "task_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_items_entidad_id",
                table: "task_items",
                column: "entidad_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_items_subcategoria_id",
                table: "task_items",
                column: "subcategoria_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_items_tenant_id_entidad_id",
                table: "task_items",
                columns: new[] { "tenant_id", "entidad_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_items_tenant_id_subcategoria_id",
                table: "task_items",
                columns: new[] { "tenant_id", "subcategoria_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_task_items_actividad_subcategorias_subcategoria_id",
                table: "task_items",
                column: "subcategoria_id",
                principalTable: "actividad_subcategorias",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_task_items_entidades_entidad_id",
                table: "task_items",
                column: "entidad_id",
                principalTable: "entidades",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_task_items_actividad_subcategorias_subcategoria_id",
                table: "task_items");

            migrationBuilder.DropForeignKey(
                name: "fk_task_items_entidades_entidad_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_entidad_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_subcategoria_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_tenant_id_entidad_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_tenant_id_subcategoria_id",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "entidad_id",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "subcategoria_id",
                table: "task_items");

            migrationBuilder.AlterColumn<Guid>(
                name: "activity_type_id",
                table: "task_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
