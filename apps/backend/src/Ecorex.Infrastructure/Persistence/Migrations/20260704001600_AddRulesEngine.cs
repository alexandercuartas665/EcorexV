using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRulesEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rule_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_code = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rule_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    verb_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    params_json = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_rules_rule_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "rule_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "form_field_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_field_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_field_rules_form_questions_form_question_id",
                        column: x => x.form_question_id,
                        principalTable: "form_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_form_field_rules_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rule_execution_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    executed_by_tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rule_name_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    trigger_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    context_json = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    records_affected = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rule_execution_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_rule_execution_logs_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_node_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_autonomous = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_node_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_node_rules_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_node_rules_workflow_nodes_workflow_node_id",
                        column: x => x.workflow_node_id,
                        principalTable: "workflow_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_field_rules_form_question_id_rule_id",
                table: "form_field_rules",
                columns: new[] { "form_question_id", "rule_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_field_rules_rule_id",
                table: "form_field_rules",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_rule_documents_tenant_id_document_code",
                table: "rule_documents",
                columns: new[] { "tenant_id", "document_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rule_documents_tenant_id_is_archived",
                table: "rule_documents",
                columns: new[] { "tenant_id", "is_archived" });

            migrationBuilder.CreateIndex(
                name: "ix_rule_execution_logs_expires_at",
                table: "rule_execution_logs",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_rule_execution_logs_rule_id",
                table: "rule_execution_logs",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_rule_execution_logs_tenant_id_rule_id_created_at",
                table: "rule_execution_logs",
                columns: new[] { "tenant_id", "rule_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_rules_document_id_sort_order",
                table: "rules",
                columns: new[] { "document_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_rules_rule_id",
                table: "workflow_node_rules",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_rules_workflow_node_id_rule_id",
                table: "workflow_node_rules",
                columns: new[] { "workflow_node_id", "rule_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_field_rules");

            migrationBuilder.DropTable(
                name: "rule_execution_logs");

            migrationBuilder.DropTable(
                name: "workflow_node_rules");

            migrationBuilder.DropTable(
                name: "rules");

            migrationBuilder.DropTable(
                name: "rule_documents");
        }
    }
}
