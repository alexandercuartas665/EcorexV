using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddFormBuilderChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_builder_conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    form_definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    started_by_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_builder_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "form_builder_messages",
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
                    table.PrimaryKey("pk_form_builder_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_builder_messages_form_builder_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "form_builder_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_builder_conversations_form_definition_id",
                table: "form_builder_conversations",
                column: "form_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_builder_conversations_tenant_id_created_at",
                table: "form_builder_conversations",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_form_builder_messages_conversation_id_sequence",
                table: "form_builder_messages",
                columns: new[] { "conversation_id", "sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_builder_messages");

            migrationBuilder.DropTable(
                name: "form_builder_conversations");
        }
    }
}
