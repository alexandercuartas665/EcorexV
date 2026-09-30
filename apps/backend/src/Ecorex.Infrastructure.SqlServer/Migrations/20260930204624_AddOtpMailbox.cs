using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddOtpMailbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "otp_mailbox_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    proveedor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    host = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    puerto = table.Column<int>(type: "int", nullable: false),
                    usar_ssl = table.Column<bool>(type: "bit", nullable: false),
                    usuario = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    password_cifrada = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    activo = table.Column<bool>(type: "bit", nullable: false),
                    ultima_validacion = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otp_mailbox_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_otp_mailbox_configs_tenant_id_nombre",
                table: "otp_mailbox_configs",
                columns: new[] { "tenant_id", "nombre" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "otp_mailbox_configs");
        }
    }
}
