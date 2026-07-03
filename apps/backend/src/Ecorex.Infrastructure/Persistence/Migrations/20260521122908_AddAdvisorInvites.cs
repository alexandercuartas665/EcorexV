using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvisorInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "invitation_expires_at",
                table: "tenant_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invitation_token",
                table: "tenant_users",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lead_visibility",
                table: "tenant_users",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_users_invitation_token",
                table: "tenant_users",
                column: "invitation_token");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tenant_users_invitation_token",
                table: "tenant_users");

            migrationBuilder.DropColumn(
                name: "invitation_expires_at",
                table: "tenant_users");

            migrationBuilder.DropColumn(
                name: "invitation_token",
                table: "tenant_users");

            migrationBuilder.DropColumn(
                name: "lead_visibility",
                table: "tenant_users");
        }
    }
}
