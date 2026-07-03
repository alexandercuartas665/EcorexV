using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    revision = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    is_archived = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "form_containers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    container_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    parent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    style = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_containers", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_containers_form_containers_parent_id",
                        column: x => x.parent_id,
                        principalTable: "form_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_form_containers_form_definitions_definition_id",
                        column: x => x.definition_id,
                        principalTable: "form_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "form_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    data = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    submitted_by_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_responses", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_responses_form_definitions_definition_id",
                        column: x => x.definition_id,
                        principalTable: "form_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "form_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    single_use = table.Column<bool>(type: "bit", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    allow_anonymous = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_tokens_form_definitions_definition_id",
                        column: x => x.definition_id,
                        principalTable: "form_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_node_forms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    node_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_node_forms", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_node_forms_form_definitions_definition_id",
                        column: x => x.definition_id,
                        principalTable: "form_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_node_forms_workflow_nodes_node_id",
                        column: x => x.node_id,
                        principalTable: "workflow_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "form_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    container_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    field_code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    label = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    caption = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    help_text = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    control_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    options_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    required = table.Column<bool>(type: "bit", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    grid_col = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    numeral = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    validation_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_questions", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_questions_form_containers_container_id",
                        column: x => x.container_id,
                        principalTable: "form_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_form_questions_form_definitions_definition_id",
                        column: x => x.definition_id,
                        principalTable: "form_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "form_flow_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    form_response_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    workflow_node_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_flow_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_flow_links_form_responses_form_response_id",
                        column: x => x.form_response_id,
                        principalTable: "form_responses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_form_flow_links_workflow_instances_workflow_instance_id",
                        column: x => x.workflow_instance_id,
                        principalTable: "workflow_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_form_flow_links_workflow_nodes_workflow_node_id",
                        column: x => x.workflow_node_id,
                        principalTable: "workflow_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_containers_definition_id_sort_order",
                table: "form_containers",
                columns: new[] { "definition_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_form_containers_parent_id",
                table: "form_containers",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_definitions_tenant_id_code",
                table: "form_definitions",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_definitions_tenant_id_is_archived",
                table: "form_definitions",
                columns: new[] { "tenant_id", "is_archived" });

            migrationBuilder.CreateIndex(
                name: "ix_form_flow_links_form_response_id_status",
                table: "form_flow_links",
                columns: new[] { "form_response_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_form_flow_links_workflow_instance_id_workflow_node_id_form_response_id",
                table: "form_flow_links",
                columns: new[] { "workflow_instance_id", "workflow_node_id", "form_response_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_flow_links_workflow_node_id",
                table: "form_flow_links",
                column: "workflow_node_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_questions_container_id",
                table: "form_questions",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_questions_definition_id_container_id_sort_order",
                table: "form_questions",
                columns: new[] { "definition_id", "container_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_form_questions_definition_id_field_code",
                table: "form_questions",
                columns: new[] { "definition_id", "field_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_responses_definition_id",
                table: "form_responses",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_responses_tenant_id_definition_id_reference",
                table: "form_responses",
                columns: new[] { "tenant_id", "definition_id", "reference" });

            migrationBuilder.CreateIndex(
                name: "ix_form_tokens_definition_id",
                table: "form_tokens",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_tokens_tenant_id_token_hash",
                table: "form_tokens",
                columns: new[] { "tenant_id", "token_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_tokens_token_hash",
                table: "form_tokens",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_forms_definition_id",
                table: "workflow_node_forms",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_forms_node_id",
                table: "workflow_node_forms",
                column: "node_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_flow_links");

            migrationBuilder.DropTable(
                name: "form_questions");

            migrationBuilder.DropTable(
                name: "form_tokens");

            migrationBuilder.DropTable(
                name: "workflow_node_forms");

            migrationBuilder.DropTable(
                name: "form_responses");

            migrationBuilder.DropTable(
                name: "form_containers");

            migrationBuilder.DropTable(
                name: "form_definitions");
        }
    }
}
