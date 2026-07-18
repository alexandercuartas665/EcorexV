using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddScrapeFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scrape_flows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    start_url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    client_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    container_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    last_run_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_result_summary = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scrape_flows", x => x.id);
                    table.ForeignKey(
                        name: "fk_scrape_flows_data_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "data_clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_scrape_flows_data_containers_container_id",
                        column: x => x.container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "scrape_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    flow_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order = table.Column<int>(type: "int", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    wait_ms = table.Column<int>(type: "int", nullable: true),
                    url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    script = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    selector = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    mapping_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    instruction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    target_container_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tool_allow_list_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    max_steps = table.Column<int>(type: "int", nullable: true),
                    max_seconds = table.Column<int>(type: "int", nullable: true),
                    ai_provider_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ai_model = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scrape_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_scrape_steps_scrape_flows_flow_id",
                        column: x => x.flow_id,
                        principalTable: "scrape_flows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scrape_variables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    flow_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    value_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_secret = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scrape_variables", x => x.id);
                    table.ForeignKey(
                        name: "fk_scrape_variables_scrape_flows_flow_id",
                        column: x => x.flow_id,
                        principalTable: "scrape_flows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_scrape_flows_client_id",
                table: "scrape_flows",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_scrape_flows_container_id",
                table: "scrape_flows",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "ix_scrape_flows_tenant_id_name",
                table: "scrape_flows",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scrape_steps_flow_id",
                table: "scrape_steps",
                column: "flow_id");

            migrationBuilder.CreateIndex(
                name: "ix_scrape_steps_tenant_id_flow_id_order",
                table: "scrape_steps",
                columns: new[] { "tenant_id", "flow_id", "order" });

            migrationBuilder.CreateIndex(
                name: "ix_scrape_variables_flow_id",
                table: "scrape_variables",
                column: "flow_id");

            migrationBuilder.CreateIndex(
                name: "ix_scrape_variables_tenant_id_flow_id_name",
                table: "scrape_variables",
                columns: new[] { "tenant_id", "flow_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scrape_steps");

            migrationBuilder.DropTable(
                name: "scrape_variables");

            migrationBuilder.DropTable(
                name: "scrape_flows");
        }
    }
}
