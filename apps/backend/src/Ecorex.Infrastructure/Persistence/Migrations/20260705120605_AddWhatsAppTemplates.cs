using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "whats_app_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    language = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    header_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    header_text = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    body_text = table.Column<string>(type: "text", nullable: false),
                    footer_text = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    variables_json = table.Column<string>(type: "jsonb", nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    whats_app_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    waba_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    provider_template_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_whats_app_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_whats_app_templates_whats_app_lines_whats_app_line_id",
                        column: x => x.whats_app_line_id,
                        principalTable: "whats_app_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_whats_app_templates_tenant_id_is_active",
                table: "whats_app_templates",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_whats_app_templates_tenant_id_name_language",
                table: "whats_app_templates",
                columns: new[] { "tenant_id", "name", "language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_whats_app_templates_tenant_id_whats_app_line_id",
                table: "whats_app_templates",
                columns: new[] { "tenant_id", "whats_app_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_whats_app_templates_whats_app_line_id",
                table: "whats_app_templates",
                column: "whats_app_line_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "whats_app_templates");
        }
    }
}
