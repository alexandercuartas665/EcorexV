using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailDeliveryEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "event_webhook_token",
                table: "tenant_email_configs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "event_webhook_token",
                table: "email_configs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "clicked_at_utc",
                table: "contact_workflow_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_detail",
                table: "contact_workflow_runs",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_status",
                table: "contact_workflow_runs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivery_updated_at_utc",
                table: "contact_workflow_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "opened_at_utc",
                table: "contact_workflow_runs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "event_webhook_token",
                table: "tenant_email_configs");

            migrationBuilder.DropColumn(
                name: "event_webhook_token",
                table: "email_configs");

            migrationBuilder.DropColumn(
                name: "clicked_at_utc",
                table: "contact_workflow_runs");

            migrationBuilder.DropColumn(
                name: "delivery_detail",
                table: "contact_workflow_runs");

            migrationBuilder.DropColumn(
                name: "delivery_status",
                table: "contact_workflow_runs");

            migrationBuilder.DropColumn(
                name: "delivery_updated_at_utc",
                table: "contact_workflow_runs");

            migrationBuilder.DropColumn(
                name: "opened_at_utc",
                table: "contact_workflow_runs");
        }
    }
}
