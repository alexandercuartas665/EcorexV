using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateAgentAndEmailUnsubscribe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "firma_html",
                table: "tenant_users",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "email_templates",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_tenant_user_id",
                table: "email_templates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "email_templates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "use_for_template_builder",
                table: "ai_provider_configs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "email_unsubscribes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    origen = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    motivo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_unsubscribes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "template_agent_conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    started_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_agent_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "template_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "template_agent_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    attachments_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tool_call_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    tool_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    tool_args_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tool_result_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    proposal_state = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_agent_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_template_agent_messages_template_agent_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "template_agent_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_unsubscribes_tenant_id_email",
                table: "email_unsubscribes",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_template_agent_conversations_tenant_id_kind_template_id",
                table: "template_agent_conversations",
                columns: new[] { "tenant_id", "kind", "template_id" });

            migrationBuilder.CreateIndex(
                name: "ix_template_agent_messages_conversation_id_sequence",
                table: "template_agent_messages",
                columns: new[] { "conversation_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_template_snapshots_tenant_id_kind_template_id_created_at",
                table: "template_snapshots",
                columns: new[] { "tenant_id", "kind", "template_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_unsubscribes");

            migrationBuilder.DropTable(
                name: "template_agent_messages");

            migrationBuilder.DropTable(
                name: "template_snapshots");

            migrationBuilder.DropTable(
                name: "template_agent_conversations");

            migrationBuilder.DropColumn(
                name: "firma_html",
                table: "tenant_users");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "email_templates");

            migrationBuilder.DropColumn(
                name: "deleted_by_tenant_user_id",
                table: "email_templates");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "email_templates");

            migrationBuilder.DropColumn(
                name: "use_for_template_builder",
                table: "ai_provider_configs");
        }
    }
}
