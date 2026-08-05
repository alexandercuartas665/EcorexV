using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAsesorAgentAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "assignable_by_agent",
                table: "asesores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_agent_assignment_at",
                table: "asesores",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_asesores_tenant_id_assignable_by_agent",
                table: "asesores",
                columns: new[] { "tenant_id", "assignable_by_agent" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_asesores_tenant_id_assignable_by_agent",
                table: "asesores");

            migrationBuilder.DropColumn(
                name: "assignable_by_agent",
                table: "asesores");

            migrationBuilder.DropColumn(
                name: "last_agent_assignment_at",
                table: "asesores");
        }
    }
}
