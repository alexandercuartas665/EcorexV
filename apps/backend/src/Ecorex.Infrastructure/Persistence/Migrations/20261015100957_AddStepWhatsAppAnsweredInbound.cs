using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStepWhatsAppAnsweredInbound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "whats_app_answered_inbound_at",
                table: "workflow_step_histories",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "whats_app_answered_inbound_at",
                table: "workflow_step_histories");
        }
    }
}
