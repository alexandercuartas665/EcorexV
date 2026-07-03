using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "workflow_instance_id",
                table: "task_items",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    process_code = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    bpmn_xml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    is_published = table.Column<bool>(type: "bit", nullable: false),
                    is_archived = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    current_cycle = table.Column<int>(type: "int", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_instances", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_instances_task_items_task_item_id",
                        column: x => x.task_item_id,
                        principalTable: "task_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_instances_workflow_definitions_definition_id",
                        column: x => x.definition_id,
                        principalTable: "workflow_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bpmn_element_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    node_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    step_number = table.Column<int>(type: "int", nullable: true),
                    allows_assignment = table.Column<bool>(type: "bit", nullable: false),
                    restart_node_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_nodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_nodes_workflow_definitions_definition_id",
                        column: x => x.definition_id,
                        principalTable: "workflow_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_workflow_nodes_workflow_nodes_restart_node_id",
                        column: x => x.restart_node_id,
                        principalTable: "workflow_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_edges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_node_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_node_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bpmn_element_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    condition_expression = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_edges", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_edges_workflow_definitions_definition_id",
                        column: x => x.definition_id,
                        principalTable: "workflow_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_workflow_edges_workflow_nodes_source_node_id",
                        column: x => x.source_node_id,
                        principalTable: "workflow_nodes",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_workflow_edges_workflow_nodes_target_node_id",
                        column: x => x.target_node_id,
                        principalTable: "workflow_nodes",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "workflow_step_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instance_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    node_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    cycle_index = table.Column<int>(type: "int", nullable: false),
                    is_current = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    assigned_to_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    executed_by_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_cycle_start = table.Column<bool>(type: "bit", nullable: false),
                    approval_result = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    approval_comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_step_histories", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_step_histories_workflow_instances_instance_id",
                        column: x => x.instance_id,
                        principalTable: "workflow_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_workflow_step_histories_workflow_nodes_node_id",
                        column: x => x.node_id,
                        principalTable: "workflow_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_items_workflow_instance_id",
                table: "task_items",
                column: "workflow_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_types_workflow_definition_id",
                table: "activity_types",
                column: "workflow_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_tenant_id_is_published",
                table: "workflow_definitions",
                columns: new[] { "tenant_id", "is_published" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_definitions_tenant_id_process_code_version",
                table: "workflow_definitions",
                columns: new[] { "tenant_id", "process_code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_edges_definition_id_source_node_id",
                table: "workflow_edges",
                columns: new[] { "definition_id", "source_node_id" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_edges_source_node_id",
                table: "workflow_edges",
                column: "source_node_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_edges_target_node_id",
                table: "workflow_edges",
                column: "target_node_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_definition_id",
                table: "workflow_instances",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_task_item_id",
                table: "workflow_instances",
                column: "task_item_id",
                unique: true,
                filter: "[task_item_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_instances_tenant_id_status",
                table: "workflow_instances",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_nodes_definition_id_bpmn_element_id",
                table: "workflow_nodes",
                columns: new[] { "definition_id", "bpmn_element_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workflow_nodes_restart_node_id",
                table: "workflow_nodes",
                column: "restart_node_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_histories_instance_id_is_current",
                table: "workflow_step_histories",
                columns: new[] { "instance_id", "is_current" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_histories_instance_id_node_id_cycle_index",
                table: "workflow_step_histories",
                columns: new[] { "instance_id", "node_id", "cycle_index" });

            migrationBuilder.CreateIndex(
                name: "ix_workflow_step_histories_node_id",
                table: "workflow_step_histories",
                column: "node_id");

            migrationBuilder.AddForeignKey(
                name: "fk_activity_types_workflow_definitions_workflow_definition_id",
                table: "activity_types",
                column: "workflow_definition_id",
                principalTable: "workflow_definitions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_task_items_workflow_instances_workflow_instance_id",
                table: "task_items",
                column: "workflow_instance_id",
                principalTable: "workflow_instances",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_activity_types_workflow_definitions_workflow_definition_id",
                table: "activity_types");

            migrationBuilder.DropForeignKey(
                name: "fk_task_items_workflow_instances_workflow_instance_id",
                table: "task_items");

            migrationBuilder.DropTable(
                name: "workflow_edges");

            migrationBuilder.DropTable(
                name: "workflow_step_histories");

            migrationBuilder.DropTable(
                name: "workflow_instances");

            migrationBuilder.DropTable(
                name: "workflow_nodes");

            migrationBuilder.DropTable(
                name: "workflow_definitions");

            migrationBuilder.DropIndex(
                name: "ix_task_items_workflow_instance_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_activity_types_workflow_definition_id",
                table: "activity_types");

            migrationBuilder.DropColumn(
                name: "workflow_instance_id",
                table: "task_items");
        }
    }
}
