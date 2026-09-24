using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddItemFieldGeneralScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_item_field_definitions_tenant_id_item_type_id_field_key",
                table: "item_field_definitions");

            migrationBuilder.AlterColumn<Guid>(
                name: "item_type_id",
                table: "item_field_definitions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "ix_item_field_definitions_tenant_id_item_type_id_field_key",
                table: "item_field_definitions",
                columns: new[] { "tenant_id", "item_type_id", "field_key" },
                unique: true,
                filter: "[item_type_id] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_item_field_definitions_tenant_id_item_type_id_field_key",
                table: "item_field_definitions");

            migrationBuilder.AlterColumn<Guid>(
                name: "item_type_id",
                table: "item_field_definitions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_field_definitions_tenant_id_item_type_id_field_key",
                table: "item_field_definitions",
                columns: new[] { "tenant_id", "item_type_id", "field_key" },
                unique: true);
        }
    }
}
