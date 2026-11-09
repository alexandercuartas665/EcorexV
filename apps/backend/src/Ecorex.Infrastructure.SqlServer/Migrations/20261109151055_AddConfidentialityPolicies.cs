using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddConfidentialityPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "confidentiality_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    titulo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    contenido = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    public_token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    activa = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_confidentiality_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "confidentiality_policy_acceptances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    identificacion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    correo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    aceptado_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    policy_version_snapshot = table.Column<long>(type: "bigint", nullable: false),
                    contenido_hash_snapshot = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_confidentiality_policy_acceptances", x => x.id);
                    table.ForeignKey(
                        name: "fk_confidentiality_policy_acceptances_confidentiality_policies_policy_id",
                        column: x => x.policy_id,
                        principalTable: "confidentiality_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_confidentiality_policies_public_token",
                table: "confidentiality_policies",
                column: "public_token");

            migrationBuilder.CreateIndex(
                name: "ix_confidentiality_policies_tenant_id",
                table: "confidentiality_policies",
                column: "tenant_id",
                unique: true,
                filter: "[is_deleted] = 0");

            migrationBuilder.CreateIndex(
                name: "ix_confidentiality_policy_acceptances_policy_id",
                table: "confidentiality_policy_acceptances",
                column: "policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_confidentiality_policy_acceptances_tenant_id_policy_id_aceptado_at",
                table: "confidentiality_policy_acceptances",
                columns: new[] { "tenant_id", "policy_id", "aceptado_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "confidentiality_policy_acceptances");

            migrationBuilder.DropTable(
                name: "confidentiality_policies");
        }
    }
}
