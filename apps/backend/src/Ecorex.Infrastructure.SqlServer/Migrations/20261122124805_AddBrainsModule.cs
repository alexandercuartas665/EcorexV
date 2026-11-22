using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddBrainsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mockup_notes_mockup_projects_mockup_project_id",
                table: "mockup_notes");

            migrationBuilder.RenameColumn(
                name: "mockup_project_id",
                table: "mockup_notes",
                newName: "brain_id");

            migrationBuilder.RenameIndex(
                name: "ix_mockup_notes_tenant_id_mockup_project_id_title_key",
                table: "mockup_notes",
                newName: "ix_mockup_notes_tenant_id_brain_id_title_key");

            migrationBuilder.RenameIndex(
                name: "ix_mockup_notes_mockup_project_id",
                table: "mockup_notes",
                newName: "ix_mockup_notes_brain_id");

            migrationBuilder.RenameColumn(
                name: "mockup_project_id",
                table: "mockup_note_links",
                newName: "brain_id");

            migrationBuilder.RenameIndex(
                name: "ix_mockup_note_links_tenant_id_mockup_project_id_target_kind_target_key",
                table: "mockup_note_links",
                newName: "ix_mockup_note_links_tenant_id_brain_id_target_kind_target_key");

            migrationBuilder.CreateTable(
                name: "brain_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    color = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brain_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "brains",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    slug = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    category_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_by_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
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
                    table.PrimaryKey("pk_brains", x => x.id);
                    table.ForeignKey(
                        name: "fk_brains_brain_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "brain_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mockup_project_brains",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mockup_project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    brain_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mockup_project_brains", x => x.id);
                    table.ForeignKey(
                        name: "fk_mockup_project_brains_brains_brain_id",
                        column: x => x.brain_id,
                        principalTable: "brains",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mockup_project_brains_mockup_projects_mockup_project_id",
                        column: x => x.mockup_project_id,
                        principalTable: "mockup_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_brain_categories_tenant_id_name",
                table: "brain_categories",
                columns: new[] { "tenant_id", "name" },
                unique: true,
                filter: "[is_deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "ix_brains_category_id",
                table: "brains",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_brains_tenant_id_category_id",
                table: "brains",
                columns: new[] { "tenant_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_brains_tenant_id_slug",
                table: "brains",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "[is_deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "ix_mockup_project_brains_brain_id",
                table: "mockup_project_brains",
                column: "brain_id");

            migrationBuilder.CreateIndex(
                name: "ix_mockup_project_brains_mockup_project_id",
                table: "mockup_project_brains",
                column: "mockup_project_id");

            migrationBuilder.CreateIndex(
                name: "ix_mockup_project_brains_tenant_id_brain_id",
                table: "mockup_project_brains",
                columns: new[] { "tenant_id", "brain_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mockup_project_brains_tenant_id_mockup_project_id_brain_id",
                table: "mockup_project_brains",
                columns: new[] { "tenant_id", "mockup_project_id", "brain_id" },
                unique: true);

            // ADR-0200: datos (mismo criterio que la migracion de PostgreSQL): cerebro con el MISMO Id del proyecto para
            // cada maqueta con notas, categoria "Maquetas" y vinculo principal.
            migrationBuilder.Sql(@"
INSERT INTO [brain_categories] ([id], [tenant_id], [name], [color], [sort_order], [is_deleted], [created_at])
SELECT NEWID(), t.[tenant_id], N'Maquetas', N'#7c3aed', 1, 0, SYSDATETIMEOFFSET()
FROM (SELECT DISTINCT p.[tenant_id] FROM [mockup_projects] p
      WHERE EXISTS (SELECT 1 FROM [mockup_notes] n WHERE n.[brain_id] = p.[id])) t;

INSERT INTO [brains] ([id], [tenant_id], [slug], [name], [description], [category_id], [created_by_name], [is_deleted], [deleted_at], [version], [created_at])
SELECT p.[id], p.[tenant_id], p.[slug], LEFT(N'Cerebro de ' + p.[name], 150), NULL,
       (SELECT TOP 1 c.[id] FROM [brain_categories] c WHERE c.[tenant_id] = p.[tenant_id] AND c.[name] = N'Maquetas' AND c.[is_deleted] = 0),
       NULL, CASE WHEN p.[deleted_at] IS NULL THEN 0 ELSE 1 END, p.[deleted_at], 0, SYSDATETIMEOFFSET()
FROM [mockup_projects] p
WHERE EXISTS (SELECT 1 FROM [mockup_notes] n WHERE n.[brain_id] = p.[id]);

INSERT INTO [mockup_project_brains] ([id], [tenant_id], [mockup_project_id], [brain_id], [sort_order], [created_at])
SELECT NEWID(), b.[tenant_id], b.[id], b.[id], 0, SYSDATETIMEOFFSET()
FROM [brains] b;
");

            migrationBuilder.AddForeignKey(
                name: "fk_mockup_notes_brains_brain_id",
                table: "mockup_notes",
                column: "brain_id",
                principalTable: "brains",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mockup_notes_brains_brain_id",
                table: "mockup_notes");

            migrationBuilder.DropTable(
                name: "mockup_project_brains");

            migrationBuilder.DropTable(
                name: "brains");

            migrationBuilder.DropTable(
                name: "brain_categories");

            migrationBuilder.RenameColumn(
                name: "brain_id",
                table: "mockup_notes",
                newName: "mockup_project_id");

            migrationBuilder.RenameIndex(
                name: "ix_mockup_notes_tenant_id_brain_id_title_key",
                table: "mockup_notes",
                newName: "ix_mockup_notes_tenant_id_mockup_project_id_title_key");

            migrationBuilder.RenameIndex(
                name: "ix_mockup_notes_brain_id",
                table: "mockup_notes",
                newName: "ix_mockup_notes_mockup_project_id");

            migrationBuilder.RenameColumn(
                name: "brain_id",
                table: "mockup_note_links",
                newName: "mockup_project_id");

            migrationBuilder.RenameIndex(
                name: "ix_mockup_note_links_tenant_id_brain_id_target_kind_target_key",
                table: "mockup_note_links",
                newName: "ix_mockup_note_links_tenant_id_mockup_project_id_target_kind_target_key");

            migrationBuilder.AddForeignKey(
                name: "fk_mockup_notes_mockup_projects_mockup_project_id",
                table: "mockup_notes",
                column: "mockup_project_id",
                principalTable: "mockup_projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
