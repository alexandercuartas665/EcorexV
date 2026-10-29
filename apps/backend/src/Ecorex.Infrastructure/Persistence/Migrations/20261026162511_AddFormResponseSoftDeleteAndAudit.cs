using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormResponseSoftDeleteAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "form_responses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_tenant_user_id",
                table: "form_responses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "form_responses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "form_response_audits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_response_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    action = table.Column<int>(type: "integer", nullable: false),
                    actor_tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_ai_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    data_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_response_audits", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_response_audits_tenant_id_created_at",
                table: "form_response_audits",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_form_response_audits_tenant_id_form_response_id",
                table: "form_response_audits",
                columns: new[] { "tenant_id", "form_response_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_response_audits");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "form_responses");

            migrationBuilder.DropColumn(
                name: "deleted_by_tenant_user_id",
                table: "form_responses");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "form_responses");
        }
    }
}
