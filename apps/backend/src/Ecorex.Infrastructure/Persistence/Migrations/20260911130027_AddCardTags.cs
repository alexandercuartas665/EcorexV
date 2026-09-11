using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCardTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "card_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    color = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "flow_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    card_tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flow_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_flow_tags_card_tags_card_tag_id",
                        column: x => x.card_tag_id,
                        principalTable: "card_tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "form_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    card_tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_tags_card_tags_card_tag_id",
                        column: x => x.card_tag_id,
                        principalTable: "card_tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_card_tags_tenant_id_scope",
                table: "card_tags",
                columns: new[] { "tenant_id", "scope" });

            migrationBuilder.CreateIndex(
                name: "ix_card_tags_tenant_id_scope_name",
                table: "card_tags",
                columns: new[] { "tenant_id", "scope", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_flow_tags_card_tag_id",
                table: "flow_tags",
                column: "card_tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_flow_tags_tenant_id_process_code",
                table: "flow_tags",
                columns: new[] { "tenant_id", "process_code" });

            migrationBuilder.CreateIndex(
                name: "ix_flow_tags_tenant_id_process_code_card_tag_id",
                table: "flow_tags",
                columns: new[] { "tenant_id", "process_code", "card_tag_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_tags_card_tag_id",
                table: "form_tags",
                column: "card_tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_tags_tenant_id_form_code",
                table: "form_tags",
                columns: new[] { "tenant_id", "form_code" });

            migrationBuilder.CreateIndex(
                name: "ix_form_tags_tenant_id_form_code_card_tag_id",
                table: "form_tags",
                columns: new[] { "tenant_id", "form_code", "card_tag_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flow_tags");

            migrationBuilder.DropTable(
                name: "form_tags");

            migrationBuilder.DropTable(
                name: "card_tags");
        }
    }
}
