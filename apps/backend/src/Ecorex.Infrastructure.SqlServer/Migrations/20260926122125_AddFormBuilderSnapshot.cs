using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddFormBuilderSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_builder_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    form_definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    trigger = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    form_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    templates_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_by_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_builder_snapshots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_builder_snapshots_form_definition_id_created_at",
                table: "form_builder_snapshots",
                columns: new[] { "form_definition_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_builder_snapshots");
        }
    }
}
