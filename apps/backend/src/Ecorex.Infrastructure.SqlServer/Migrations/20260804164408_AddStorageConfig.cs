using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "storage_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    connection_string_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    container_name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    last_validated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_configs", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "storage_configs");
        }
    }
}
