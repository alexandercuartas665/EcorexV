using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddRequerimientoSolicitante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "solicitante_nombre",
                table: "requerimientos_compra",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "solicitante_user_id",
                table: "requerimientos_compra",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_task_item_id",
                table: "requerimientos_compra",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "solicitante_nombre",
                table: "requerimientos_compra");

            migrationBuilder.DropColumn(
                name: "solicitante_user_id",
                table: "requerimientos_compra");

            migrationBuilder.DropColumn(
                name: "source_task_item_id",
                table: "requerimientos_compra");
        }
    }
}
