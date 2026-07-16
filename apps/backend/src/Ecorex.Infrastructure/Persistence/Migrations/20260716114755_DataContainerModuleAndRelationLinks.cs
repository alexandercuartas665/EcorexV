using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DataContainerModuleAndRelationLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "filter_columns_json",
                table: "data_containers",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "list_columns_json",
                table: "data_containers",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "menu_node_id",
                table: "data_containers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "module_icon",
                table: "data_containers",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "module_route",
                table: "data_containers",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "data_model_relation_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    relation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_row_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_row_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_model_relation_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_model_relation_links_data_container_rows_from_row_id",
                        column: x => x.from_row_id,
                        principalTable: "data_container_rows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_model_relation_links_data_container_rows_to_row_id",
                        column: x => x.to_row_id,
                        principalTable: "data_container_rows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_model_relation_links_data_model_relations_relation_id",
                        column: x => x.relation_id,
                        principalTable: "data_model_relations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_containers_menu_node_id",
                table: "data_containers",
                column: "menu_node_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_containers_tenant_id_module_route",
                table: "data_containers",
                columns: new[] { "tenant_id", "module_route" },
                unique: true,
                filter: "module_route IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_data_model_relation_links_from_row_id",
                table: "data_model_relation_links",
                column: "from_row_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_model_relation_links_relation_id",
                table: "data_model_relation_links",
                column: "relation_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_model_relation_links_relation_id_from_row_id",
                table: "data_model_relation_links",
                columns: new[] { "relation_id", "from_row_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_model_relation_links_relation_id_from_row_id_to_row_id",
                table: "data_model_relation_links",
                columns: new[] { "relation_id", "from_row_id", "to_row_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_model_relation_links_tenant_id_relation_id",
                table: "data_model_relation_links",
                columns: new[] { "tenant_id", "relation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_model_relation_links_to_row_id",
                table: "data_model_relation_links",
                column: "to_row_id");

            migrationBuilder.AddForeignKey(
                name: "fk_data_containers_menu_nodes_menu_node_id",
                table: "data_containers",
                column: "menu_node_id",
                principalTable: "menu_nodes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_data_containers_menu_nodes_menu_node_id",
                table: "data_containers");

            migrationBuilder.DropTable(
                name: "data_model_relation_links");

            migrationBuilder.DropIndex(
                name: "ix_data_containers_menu_node_id",
                table: "data_containers");

            migrationBuilder.DropIndex(
                name: "ix_data_containers_tenant_id_module_route",
                table: "data_containers");

            migrationBuilder.DropColumn(
                name: "filter_columns_json",
                table: "data_containers");

            migrationBuilder.DropColumn(
                name: "list_columns_json",
                table: "data_containers");

            migrationBuilder.DropColumn(
                name: "menu_node_id",
                table: "data_containers");

            migrationBuilder.DropColumn(
                name: "module_icon",
                table: "data_containers");

            migrationBuilder.DropColumn(
                name: "module_route",
                table: "data_containers");
        }
    }
}
