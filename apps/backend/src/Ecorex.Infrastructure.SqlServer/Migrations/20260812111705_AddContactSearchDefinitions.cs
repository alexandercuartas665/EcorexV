using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddContactSearchDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_search_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    query = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    sub_query = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    country = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    region = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    city = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    extraction_prompt = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    client_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    classifier_ai_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_search_definitions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contact_search_definitions_tenant_id_name",
                table: "contact_search_definitions",
                columns: new[] { "tenant_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_search_definitions");
        }
    }
}
