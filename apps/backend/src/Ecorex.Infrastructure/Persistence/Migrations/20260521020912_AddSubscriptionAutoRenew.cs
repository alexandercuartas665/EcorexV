using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionAutoRenew : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_renew",
                table: "tenant_subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "failed_attempts",
                table: "tenant_subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "payment_method_label",
                table: "tenant_subscriptions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "wompi_payment_source_id",
                table: "tenant_subscriptions",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_renew",
                table: "tenant_subscriptions");

            migrationBuilder.DropColumn(
                name: "failed_attempts",
                table: "tenant_subscriptions");

            migrationBuilder.DropColumn(
                name: "payment_method_label",
                table: "tenant_subscriptions");

            migrationBuilder.DropColumn(
                name: "wompi_payment_source_id",
                table: "tenant_subscriptions");
        }
    }
}
