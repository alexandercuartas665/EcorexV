using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddMockupBrain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mockup_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mockup_project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    title_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    author_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    author_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    updated_by_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    imported_from = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mockup_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_mockup_notes_mockup_projects_mockup_project_id",
                        column: x => x.mockup_project_id,
                        principalTable: "mockup_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mockup_note_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mockup_project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_note_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    target_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mockup_note_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_mockup_note_links_mockup_notes_from_note_id",
                        column: x => x.from_note_id,
                        principalTable: "mockup_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mockup_note_links_from_note_id",
                table: "mockup_note_links",
                column: "from_note_id");

            migrationBuilder.CreateIndex(
                name: "ix_mockup_note_links_tenant_id_from_note_id",
                table: "mockup_note_links",
                columns: new[] { "tenant_id", "from_note_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mockup_note_links_tenant_id_mockup_project_id_target_kind_target_key",
                table: "mockup_note_links",
                columns: new[] { "tenant_id", "mockup_project_id", "target_kind", "target_key" });

            migrationBuilder.CreateIndex(
                name: "ix_mockup_notes_mockup_project_id",
                table: "mockup_notes",
                column: "mockup_project_id");

            migrationBuilder.CreateIndex(
                name: "ix_mockup_notes_tenant_id_mockup_project_id_title_key",
                table: "mockup_notes",
                columns: new[] { "tenant_id", "mockup_project_id", "title_key" },
                unique: true,
                filter: "[is_deleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mockup_note_links");

            migrationBuilder.DropTable(
                name: "mockup_notes");
        }
    }
}
