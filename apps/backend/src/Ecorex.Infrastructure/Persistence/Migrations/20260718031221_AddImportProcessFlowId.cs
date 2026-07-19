using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportProcessFlowId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "flow_id",
                table: "import_processes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_processes_tenant_id_flow_id",
                table: "import_processes",
                columns: new[] { "tenant_id", "flow_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_import_processes_tenant_id_flow_id",
                table: "import_processes");

            migrationBuilder.DropColumn(
                name: "flow_id",
                table: "import_processes");
        }
    }
}
