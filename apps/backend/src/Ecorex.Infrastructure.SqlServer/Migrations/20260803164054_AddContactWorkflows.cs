using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddContactWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_workflows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tercero_filtro_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    activo = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_workflows", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_workflows_tercero_filtros_tercero_filtro_id",
                        column: x => x.tercero_filtro_id,
                        principalTable: "tercero_filtros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contact_workflow_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contact_workflow_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    step_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    label = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    orden = table.Column<int>(type: "int", nullable: false),
                    params_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_workflow_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_workflow_steps_contact_workflows_contact_workflow_id",
                        column: x => x.contact_workflow_id,
                        principalTable: "contact_workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact_workflow_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contact_workflow_step_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    start_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    active_days = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    template_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    repeat_every = table.Column<int>(type: "int", nullable: true),
                    package_size = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_workflow_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_workflow_schedules_contact_workflow_steps_contact_workflow_step_id",
                        column: x => x.contact_workflow_step_id,
                        principalTable: "contact_workflow_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contact_workflow_schedules_contact_workflow_step_id",
                table: "contact_workflow_schedules",
                column: "contact_workflow_step_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_workflow_steps_contact_workflow_id_orden",
                table: "contact_workflow_steps",
                columns: new[] { "contact_workflow_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_contact_workflows_tenant_id_tercero_filtro_id",
                table: "contact_workflows",
                columns: new[] { "tenant_id", "tercero_filtro_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contact_workflows_tercero_filtro_id",
                table: "contact_workflows",
                column: "tercero_filtro_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_workflow_schedules");

            migrationBuilder.DropTable(
                name: "contact_workflow_steps");

            migrationBuilder.DropTable(
                name: "contact_workflows");
        }
    }
}
