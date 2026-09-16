using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentReactivacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reactivacion_ultimo_envio_at",
                table: "conversations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reactivacion_ultimo_paso",
                table: "conversations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "reactivacion_json",
                table: "ai_agents",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reactivacion_ultimo_envio_at",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "reactivacion_ultimo_paso",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "reactivacion_json",
                table: "ai_agents");
        }
    }
}
