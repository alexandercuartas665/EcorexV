using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "menu_view_id",
                table: "tenant_users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "menu_views",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_views", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "menu_nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    menu_view_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    icon_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    legacy_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    route = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    help_text = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_visible = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_nodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_menu_nodes_menu_nodes_parent_id",
                        column: x => x.parent_id,
                        principalTable: "menu_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_menu_nodes_menu_views_menu_view_id",
                        column: x => x.menu_view_id,
                        principalTable: "menu_views",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_users_menu_view_id",
                table: "tenant_users",
                column: "menu_view_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_nodes_menu_view_id_parent_id_sort_order",
                table: "menu_nodes",
                columns: new[] { "menu_view_id", "parent_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_menu_nodes_parent_id",
                table: "menu_nodes",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_menu_nodes_tenant_id_menu_view_id",
                table: "menu_nodes",
                columns: new[] { "tenant_id", "menu_view_id" });

            migrationBuilder.CreateIndex(
                name: "ix_menu_views_tenant_id_is_default",
                table: "menu_views",
                columns: new[] { "tenant_id", "is_default" });

            migrationBuilder.CreateIndex(
                name: "ix_menu_views_tenant_id_name",
                table: "menu_views",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_tenant_users_menu_views_menu_view_id",
                table: "tenant_users",
                column: "menu_view_id",
                principalTable: "menu_views",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tenant_users_menu_views_menu_view_id",
                table: "tenant_users");

            migrationBuilder.DropTable(
                name: "menu_nodes");

            migrationBuilder.DropTable(
                name: "menu_views");

            migrationBuilder.DropIndex(
                name: "ix_tenant_users_menu_view_id",
                table: "tenant_users");

            migrationBuilder.DropColumn(
                name: "menu_view_id",
                table: "tenant_users");
        }
    }
}
