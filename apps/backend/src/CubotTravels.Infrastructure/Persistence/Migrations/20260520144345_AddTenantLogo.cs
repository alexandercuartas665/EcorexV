using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CubotTravels.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantLogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "logo_url",
                table: "tenants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "logo_url",
                table: "tenants");
        }
    }
}
