using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddBrainTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_locked",
                table: "brains",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "source_template_id",
                table: "brains",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "template_version",
                table: "brains",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "brain_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    category_name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    category_color = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    is_locked = table.Column<bool>(type: "bit", nullable: false),
                    target_mode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    target_ids_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    published_version = table.Column<int>(type: "int", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    is_archived = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brain_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "brain_template_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    title_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brain_template_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_brain_template_notes_brain_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "brain_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_brains_tenant_id_source_template_id",
                table: "brains",
                columns: new[] { "tenant_id", "source_template_id" });

            migrationBuilder.CreateIndex(
                name: "ix_brain_template_notes_template_id_title_key",
                table: "brain_template_notes",
                columns: new[] { "template_id", "title_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "brain_template_notes");

            migrationBuilder.DropTable(
                name: "brain_templates");

            migrationBuilder.DropIndex(
                name: "ix_brains_tenant_id_source_template_id",
                table: "brains");

            migrationBuilder.DropColumn(
                name: "is_locked",
                table: "brains");

            migrationBuilder.DropColumn(
                name: "source_template_id",
                table: "brains");

            migrationBuilder.DropColumn(
                name: "template_version",
                table: "brains");
        }
    }
}
