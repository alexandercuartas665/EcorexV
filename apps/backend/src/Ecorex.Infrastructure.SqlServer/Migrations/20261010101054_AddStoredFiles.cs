using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stored_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    module = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ref1 = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    ref2 = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    file_name = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    extension = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    blob_path = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_files", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_tenant_id_module_ref1",
                table: "stored_files",
                columns: new[] { "tenant_id", "module", "ref1" });

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_tenant_id_module_ref1_ref2_file_name",
                table: "stored_files",
                columns: new[] { "tenant_id", "module", "ref1", "ref2", "file_name" },
                unique: true,
                filter: "[ref1] IS NOT NULL AND [ref2] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stored_files");
        }
    }
}
