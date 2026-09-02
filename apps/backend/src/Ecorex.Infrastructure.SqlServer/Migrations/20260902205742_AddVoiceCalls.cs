using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "retell_agent_maps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    prompt_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    retell_llm_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    retell_agent_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ai_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retell_agent_maps", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "retell_voice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    retell_api_key_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    from_number = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    termination_uri = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    sip_username = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    sip_password_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    voice_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    language = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    is_default = table.Column<bool>(type: "bit", nullable: false),
                    last_validated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retell_voice_lines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "voice_calls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    call_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    retell_voice_line_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    retell_agent_id = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    from_number = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    to_number = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    status = table.Column<int>(type: "int", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    duration_seconds = table.Column<int>(type: "int", nullable: true),
                    cost_usd = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    transcript_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    analysis_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ai_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    objetivo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    formularios_permitidos_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    contact_workflow_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    error_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voice_calls", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_retell_agent_maps_tenant_id_prompt_hash",
                table: "retell_agent_maps",
                columns: new[] { "tenant_id", "prompt_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_retell_voice_lines_tenant_id",
                table: "retell_voice_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_voice_calls_tenant_id_call_id",
                table: "voice_calls",
                columns: new[] { "tenant_id", "call_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "retell_agent_maps");

            migrationBuilder.DropTable(
                name: "retell_voice_lines");

            migrationBuilder.DropTable(
                name: "voice_calls");
        }
    }
}
