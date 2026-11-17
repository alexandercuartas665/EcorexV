using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalTaskLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_task_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    external_key = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    task_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    last_capture_sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    received_count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_task_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_external_task_links_task_items_task_item_id",
                        column: x => x.task_item_id,
                        principalTable: "task_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_task_links_task_item_id",
                table: "external_task_links",
                column: "task_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_task_links_tenant_id_source_external_key",
                table: "external_task_links",
                columns: new[] { "tenant_id", "source", "external_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_task_links_tenant_id_task_item_id",
                table: "external_task_links",
                columns: new[] { "tenant_id", "task_item_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_task_links");
        }
    }
}
