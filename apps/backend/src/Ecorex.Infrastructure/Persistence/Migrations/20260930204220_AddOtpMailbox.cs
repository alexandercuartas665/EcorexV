using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    proveedor = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    host = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    puerto = table.Column<int>(type: "integer", nullable: false),
                    usar_ssl = table.Column<bool>(type: "boolean", nullable: false),
                    usuario = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_cifrada = table.Column<string>(type: "text", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    ultima_validacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
