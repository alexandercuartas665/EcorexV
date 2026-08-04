using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantEmailConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_email_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    smtp_host = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    smtp_port = table.Column<int>(type: "int", nullable: false),
                    smtp_user = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    smtp_password_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    use_ssl = table.Column<bool>(type: "bit", nullable: false),
                    from_email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    from_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    last_validated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_email_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_email_configs_tenant_id",
                table: "tenant_email_configs",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_email_configs");
        }
    }
}
