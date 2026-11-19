using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceAgentJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "conversation_id",
                table: "ai_agent_run_logs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "voice_call_id",
                table: "ai_agent_run_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_agent_run_logs_tenant_id_voice_call_id_occurred_at",
                table: "ai_agent_run_logs",
                columns: new[] { "tenant_id", "voice_call_id", "occurred_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_ai_agent_run_log_conv_xor_voice",
                table: "ai_agent_run_logs",
                sql: "(conversation_id IS NOT NULL AND voice_call_id IS NULL) OR (conversation_id IS NULL AND voice_call_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ai_agent_run_logs_tenant_id_voice_call_id_occurred_at",
                table: "ai_agent_run_logs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ai_agent_run_log_conv_xor_voice",
                table: "ai_agent_run_logs");

            migrationBuilder.DropColumn(
                name: "voice_call_id",
                table: "ai_agent_run_logs");

            migrationBuilder.AlterColumn<Guid>(
                name: "conversation_id",
                table: "ai_agent_run_logs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
