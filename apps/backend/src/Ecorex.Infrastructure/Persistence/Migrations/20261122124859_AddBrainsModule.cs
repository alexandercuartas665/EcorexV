using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
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
                name: "ix_mockup_note_links_tenant_id_mockup_project_id_target_kind_t",
                table: "mockup_note_links",
                newName: "ix_mockup_note_links_tenant_id_brain_id_target_kind_target_key");

            migrationBuilder.CreateTable(
                name: "brain_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    color = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brain_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "brains",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mockup_project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    brain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                filter: "is_deleted = false");

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
                filter: "is_deleted = false");

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

            // ADR-0200: datos. Cada maqueta que ya tenia notas pasa a tener su cerebro "Cerebro de {nombre}" con el MISMO
            // Id que el proyecto (las notas ya apuntan a ese Id tras renombrar la columna, y los adjuntos del gestor de
            // archivos usan el Id como llave del duenio, asi no se mueve ningun binario), en la categoria "Maquetas" y
            // vinculado como cerebro principal. Idempotente por construccion (solo corre una vez con la migracion).
            migrationBuilder.Sql(@"
INSERT INTO brain_categories (id, tenant_id, name, color, sort_order, is_deleted, created_at)
SELECT gen_random_uuid(), t.tenant_id, 'Maquetas', '#7c3aed', 1, false, now()
FROM (SELECT DISTINCT p.tenant_id FROM mockup_projects p
      WHERE EXISTS (SELECT 1 FROM mockup_notes n WHERE n.brain_id = p.id)) t;

INSERT INTO brains (id, tenant_id, slug, name, description, category_id, created_by_name, is_deleted, deleted_at, version, created_at)
SELECT p.id, p.tenant_id, p.slug, left('Cerebro de ' || p.name, 150), NULL,
       (SELECT c.id FROM brain_categories c WHERE c.tenant_id = p.tenant_id AND c.name = 'Maquetas' AND c.is_deleted = false LIMIT 1),
       NULL, p.deleted_at IS NOT NULL, p.deleted_at, 0, now()
FROM mockup_projects p
WHERE EXISTS (SELECT 1 FROM mockup_notes n WHERE n.brain_id = p.id);

INSERT INTO mockup_project_brains (id, tenant_id, mockup_project_id, brain_id, sort_order, created_at)
SELECT gen_random_uuid(), b.tenant_id, b.id, b.id, 0, now()
FROM brains b;
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
                newName: "ix_mockup_note_links_tenant_id_mockup_project_id_target_kind_t");

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
