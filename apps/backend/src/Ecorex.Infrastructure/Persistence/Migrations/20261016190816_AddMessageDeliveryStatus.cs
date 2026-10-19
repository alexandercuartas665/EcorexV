using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageDeliveryStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "delivery_error",
                table: "messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_status",
                table: "messages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delivery_error",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "delivery_status",
                table: "messages");
        }
    }
}
