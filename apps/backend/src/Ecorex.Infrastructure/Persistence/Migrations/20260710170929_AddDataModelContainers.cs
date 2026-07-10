using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataModelContainers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_clients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    client_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    client_secret_encrypted = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_clients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_models",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_models", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_containers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    canvas_x = table.Column<double>(type: "double precision", nullable: false),
                    canvas_y = table.Column<double>(type: "double precision", nullable: false),
                    source_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    parent_container_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_field_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_containers", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_containers_data_containers_parent_container_id",
                        column: x => x.parent_container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_data_containers_data_models_model_id",
                        column: x => x.model_id,
                        principalTable: "data_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_destinations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    db_engine = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    port = table.Column<int>(type: "integer", nullable: true),
                    database_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    username = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    credentials_encrypted = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_destinations", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_destinations_data_models_model_id",
                        column: x => x.model_id,
                        principalTable: "data_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_connectors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    container_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    endpoint_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    http_method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    auth_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    db_engine = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    port = table.Column<int>(type: "integer", nullable: true),
                    database_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    username = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    credentials_encrypted = table.Column<string>(type: "text", nullable: true),
                    mapping_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_connectors", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_connectors_data_containers_container_id",
                        column: x => x.container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_data_connectors_data_models_model_id",
                        column: x => x.model_id,
                        principalTable: "data_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_container_columns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    child_container_id = table.Column<Guid>(type: "uuid", nullable: true),
                    referenced_container_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_container_columns", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_container_columns_data_containers_child_container_id",
                        column: x => x.child_container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_data_container_columns_data_containers_container_id",
                        column: x => x.container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_data_container_columns_data_containers_referenced_container",
                        column: x => x.referenced_container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "data_container_rows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_row_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_field_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_container_rows", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_container_rows_data_container_rows_parent_row_id",
                        column: x => x.parent_row_id,
                        principalTable: "data_container_rows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_data_container_rows_data_containers_container_id",
                        column: x => x.container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_processes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    container_id = table.Column<Guid>(type: "uuid", nullable: true),
                    connector_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    schedule_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    interval_minutes = table.Column<int>(type: "integer", nullable: true),
                    cron_expression = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_processes", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_processes_data_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "data_clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_import_processes_data_connectors_connector_id",
                        column: x => x.connector_id,
                        principalTable: "data_connectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_import_processes_data_containers_container_id",
                        column: x => x.container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_import_processes_data_models_model_id",
                        column: x => x.model_id,
                        principalTable: "data_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_container_cells",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_id = table.Column<Guid>(type: "uuid", nullable: false),
                    column_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_container_cells", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_container_cells_data_container_columns_column_id",
                        column: x => x.column_id,
                        principalTable: "data_container_columns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_data_container_cells_data_container_rows_row_id",
                        column: x => x.row_id,
                        principalTable: "data_container_rows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_container_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    column_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_row_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_container_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_container_links_data_container_columns_column_id",
                        column: x => x.column_id,
                        principalTable: "data_container_columns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_data_container_links_data_container_rows_row_id",
                        column: x => x.row_id,
                        principalTable: "data_container_rows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_data_container_links_data_container_rows_target_row_id",
                        column: x => x.target_row_id,
                        principalTable: "data_container_rows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_clients_tenant_id_client_id",
                table: "data_clients",
                columns: new[] { "tenant_id", "client_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_clients_tenant_id_is_active",
                table: "data_clients",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_data_connectors_container_id",
                table: "data_connectors",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_connectors_model_id",
                table: "data_connectors",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_connectors_tenant_id_model_id",
                table: "data_connectors",
                columns: new[] { "tenant_id", "model_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_container_cells_column_id",
                table: "data_container_cells",
                column: "column_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_container_cells_row_id_column_id",
                table: "data_container_cells",
                columns: new[] { "row_id", "column_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_container_columns_child_container_id",
                table: "data_container_columns",
                column: "child_container_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_container_columns_container_id_name",
                table: "data_container_columns",
                columns: new[] { "container_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_container_columns_referenced_container_id",
                table: "data_container_columns",
                column: "referenced_container_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_container_columns_tenant_id_container_id_sort_order",
                table: "data_container_columns",
                columns: new[] { "tenant_id", "container_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_data_container_links_column_id_row_id_target_row_id",
                table: "data_container_links",
                columns: new[] { "column_id", "row_id", "target_row_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_container_links_row_id",
                table: "data_container_links",
                column: "row_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_container_links_target_row_id",
                table: "data_container_links",
                column: "target_row_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_container_links_tenant_id_target_row_id",
                table: "data_container_links",
                columns: new[] { "tenant_id", "target_row_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_container_rows_container_id",
                table: "data_container_rows",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_container_rows_parent_row_id_parent_field_id",
                table: "data_container_rows",
                columns: new[] { "parent_row_id", "parent_field_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_container_rows_tenant_id_container_id_created_at",
                table: "data_container_rows",
                columns: new[] { "tenant_id", "container_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_data_containers_model_id_name",
                table: "data_containers",
                columns: new[] { "model_id", "name" },
                unique: true,
                filter: "model_id IS NOT NULL AND parent_container_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_data_containers_parent_container_id",
                table: "data_containers",
                column: "parent_container_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_containers_tenant_id_model_id",
                table: "data_containers",
                columns: new[] { "tenant_id", "model_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_containers_tenant_id_parent_container_id",
                table: "data_containers",
                columns: new[] { "tenant_id", "parent_container_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_destinations_model_id",
                table: "data_destinations",
                column: "model_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_models_tenant_id_name",
                table: "data_models",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_processes_client_id",
                table: "import_processes",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_processes_connector_id",
                table: "import_processes",
                column: "connector_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_processes_container_id",
                table: "import_processes",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_processes_model_id",
                table: "import_processes",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_processes_tenant_id_model_id",
                table: "import_processes",
                columns: new[] { "tenant_id", "model_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_container_cells");

            migrationBuilder.DropTable(
                name: "data_container_links");

            migrationBuilder.DropTable(
                name: "data_destinations");

            migrationBuilder.DropTable(
                name: "import_processes");

            migrationBuilder.DropTable(
                name: "data_container_columns");

            migrationBuilder.DropTable(
                name: "data_container_rows");

            migrationBuilder.DropTable(
                name: "data_clients");

            migrationBuilder.DropTable(
                name: "data_connectors");

            migrationBuilder.DropTable(
                name: "data_containers");

            migrationBuilder.DropTable(
                name: "data_models");
        }
    }
}
