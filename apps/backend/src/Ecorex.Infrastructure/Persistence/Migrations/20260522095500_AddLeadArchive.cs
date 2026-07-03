using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "archive_note",
                table: "leads",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "archive_reason",
                table: "leads",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                table: "leads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "archived_by_name",
                table: "leads",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_leads_tenant_id_archived_at",
                table: "leads",
                columns: new[] { "tenant_id", "archived_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_leads_tenant_id_archived_at",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "archive_note",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "archive_reason",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "archived_at",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "archived_by_name",
                table: "leads");
        }
    }
}
