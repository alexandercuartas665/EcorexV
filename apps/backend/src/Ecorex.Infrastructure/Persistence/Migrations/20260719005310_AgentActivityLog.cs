using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgentActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_activity_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    client_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    origin = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    result = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    detail = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_activity_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_activity_logs_tenant_id_client_id_started_at",
                table: "agent_activity_logs",
                columns: new[] { "tenant_id", "client_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_activity_logs_tenant_id_started_at",
                table: "agent_activity_logs",
                columns: new[] { "tenant_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_activity_logs");
        }
    }
}
