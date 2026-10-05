using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddOtpMailboxOAuth2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "auth_mode",
                table: "otp_mailbox_configs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Basic");

            migrationBuilder.AddColumn<string>(
                name: "oauth_client_id",
                table: "otp_mailbox_configs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "oauth_tenant_id",
                table: "otp_mailbox_configs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auth_mode",
                table: "otp_mailbox_configs");

            migrationBuilder.DropColumn(
                name: "oauth_client_id",
                table: "otp_mailbox_configs");

            migrationBuilder.DropColumn(
                name: "oauth_tenant_id",
                table: "otp_mailbox_configs");
        }
    }
}
