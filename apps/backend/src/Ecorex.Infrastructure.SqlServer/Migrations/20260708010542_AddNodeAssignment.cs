using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "classifier",
                table: "org_units",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Dependencia");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_user_id",
                table: "org_units",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "workflow_node_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    workflow_node_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workflow_node_policies", x => x.id);
                    table.ForeignKey(
                        name: "fk_workflow_node_policies_org_units_org_unit_id",
                        column: x => x.org_unit_id,
                        principalTable: "org_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workflow_node_policies_workflow_nodes_workflow_node_id",
                        column: x => x.workflow_node_id,
                        principalTable: "workflow_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_org_units_tenant_user_id",
                table: "org_units",
                column: "tenant_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_policies_org_unit_id",
                table: "workflow_node_policies",
                column: "org_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_workflow_node_policies_workflow_node_id_org_unit_id",
                table: "workflow_node_policies",
                columns: new[] { "workflow_node_id", "org_unit_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_node_policies");

            migrationBuilder.DropIndex(
                name: "ix_org_units_tenant_user_id",
                table: "org_units");

            migrationBuilder.DropColumn(
                name: "classifier",
                table: "org_units");

            migrationBuilder.DropColumn(
                name: "tenant_user_id",
                table: "org_units");
        }
    }
}
