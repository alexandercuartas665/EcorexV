using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardAutoArchiveAndNodeCloseReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "close_reason",
                table: "workflow_nodes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            // defaultValue 3: SIEMBRA (ADR-0123, rollout) los tableros YA existentes con 3 dias. Los tableros
            // NUEVOS reciben 15 por el inicializador de la entidad (EF siempre envia el valor en el INSERT).
            migrationBuilder.AddColumn<int>(
                name: "auto_archive_done_days",
                table: "task_boards",
                type: "int",
                nullable: false,
                defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "close_reason",
                table: "workflow_nodes");

            migrationBuilder.DropColumn(
                name: "auto_archive_done_days",
                table: "task_boards");
        }
    }
}
