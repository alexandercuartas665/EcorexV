using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "marketplace_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<int>(type: "int", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    image_ref = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    category = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    snapshot_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    snapshot_format_version = table.Column<int>(type: "int", nullable: false),
                    source_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    import_count = table.Column<int>(type: "int", nullable: false),
                    published_by_platform_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_marketplace_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_marketplace_items_category",
                table: "marketplace_items",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_marketplace_items_kind_is_active",
                table: "marketplace_items",
                columns: new[] { "kind", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "marketplace_items");
        }
    }
}
