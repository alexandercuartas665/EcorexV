using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddMockupProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mockup_projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    slug = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    current_version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    last_version_number = table.Column<int>(type: "int", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mockup_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mockup_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mockup_project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    number = table.Column<int>(type: "int", nullable: false),
                    zip_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    file_count = table.Column<int>(type: "int", nullable: false),
                    root_prefix = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    entry_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    has_anotador = table.Column<bool>(type: "bit", nullable: false),
                    notes = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    uploaded_by_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mockup_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_mockup_versions_mockup_projects_mockup_project_id",
                        column: x => x.mockup_project_id,
                        principalTable: "mockup_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mockup_projects_tenant_id_slug",
                table: "mockup_projects",
                columns: new[] { "tenant_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mockup_versions_mockup_project_id",
                table: "mockup_versions",
                column: "mockup_project_id");

            migrationBuilder.CreateIndex(
                name: "ix_mockup_versions_tenant_id_mockup_project_id_number",
                table: "mockup_versions",
                columns: new[] { "tenant_id", "mockup_project_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mockup_versions");

            migrationBuilder.DropTable(
                name: "mockup_projects");
        }
    }
}
