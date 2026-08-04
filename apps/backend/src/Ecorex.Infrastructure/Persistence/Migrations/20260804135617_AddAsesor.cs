using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAsesor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "vendedor_asesor_id",
                table: "terceros",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "asesores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    documento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    telefono = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asesores", x => x.id);
                    table.ForeignKey(
                        name: "fk_asesores_tenant_users_tenant_user_id",
                        column: x => x.tenant_user_id,
                        principalTable: "tenant_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_terceros_vendedor_asesor_id",
                table: "terceros",
                column: "vendedor_asesor_id");

            migrationBuilder.CreateIndex(
                name: "ix_asesores_tenant_id_is_active",
                table: "asesores",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_asesores_tenant_user_id",
                table: "asesores",
                column: "tenant_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_terceros_asesores_vendedor_asesor_id",
                table: "terceros",
                column: "vendedor_asesor_id",
                principalTable: "asesores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_terceros_asesores_vendedor_asesor_id",
                table: "terceros");

            migrationBuilder.DropTable(
                name: "asesores");

            migrationBuilder.DropIndex(
                name: "ix_terceros_vendedor_asesor_id",
                table: "terceros");

            migrationBuilder.DropColumn(
                name: "vendedor_asesor_id",
                table: "terceros");
        }
    }
}
