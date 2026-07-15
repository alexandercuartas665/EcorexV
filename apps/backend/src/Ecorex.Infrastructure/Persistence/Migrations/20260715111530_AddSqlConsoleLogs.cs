using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSqlConsoleLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sql_console_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    query = table.Column<string>(type: "text", nullable: false),
                    query_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    rows_affected = table.Column<int>(type: "integer", nullable: true),
                    rows_returned = table.Column<int>(type: "integer", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sql_console_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sql_console_logs_executed_at",
                table: "sql_console_logs",
                column: "executed_at");

            migrationBuilder.CreateIndex(
                name: "ix_sql_console_logs_tenant_id",
                table: "sql_console_logs",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sql_console_logs");
        }
    }
}
