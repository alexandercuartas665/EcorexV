using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorHeadersAndTokenExchange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "headers_json",
                table: "data_connectors",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "token_exchange_json",
                table: "data_connectors",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "headers_json",
                table: "data_connectors");

            migrationBuilder.DropColumn(
                name: "token_exchange_json",
                table: "data_connectors");
        }
    }
}
