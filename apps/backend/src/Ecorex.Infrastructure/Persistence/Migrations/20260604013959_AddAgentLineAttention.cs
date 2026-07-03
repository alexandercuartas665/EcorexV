using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentLineAttention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_conversations_tenant_id_contact_phone",
                table: "conversations");

            migrationBuilder.CreateTable(
                name: "ai_agent_line_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    whats_app_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_connected = table.Column<bool>(type: "boolean", nullable: false),
                    auto_confirm = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_agent_line_bindings", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_agent_line_bindings_ai_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ai_agent_line_bindings_whats_app_lines_whats_app_line_id",
                        column: x => x.whats_app_line_id,
                        principalTable: "whats_app_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_agent_run_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    response = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_agent_run_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_id_whats_app_line_id_contact_phone",
                table: "conversations",
                columns: new[] { "tenant_id", "whats_app_line_id", "contact_phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_line_bindings_agent_id",
                table: "ai_agent_line_bindings",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_line_bindings_tenant_id_agent_id",
                table: "ai_agent_line_bindings",
                columns: new[] { "tenant_id", "agent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_line_bindings_tenant_id_whats_app_line_id",
                table: "ai_agent_line_bindings",
                columns: new[] { "tenant_id", "whats_app_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_line_bindings_whats_app_line_id",
                table: "ai_agent_line_bindings",
                column: "whats_app_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_run_logs_tenant_id_conversation_id_occurred_at",
                table: "ai_agent_run_logs",
                columns: new[] { "tenant_id", "conversation_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_agent_line_bindings");

            migrationBuilder.DropTable(
                name: "ai_agent_run_logs");

            migrationBuilder.DropIndex(
                name: "ix_conversations_tenant_id_whats_app_line_id_contact_phone",
                table: "conversations");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_id_contact_phone",
                table: "conversations",
                columns: new[] { "tenant_id", "contact_phone" },
                unique: true);
        }
    }
}
