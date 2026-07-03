using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HairRefImageInDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "url",
                table: "hair_length_reference_images");

            migrationBuilder.AddColumn<byte[]>(
                name: "content",
                table: "hair_length_reference_images",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "content_type",
                table: "hair_length_reference_images",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "content",
                table: "hair_length_reference_images");

            migrationBuilder.DropColumn(
                name: "content_type",
                table: "hair_length_reference_images");

            migrationBuilder.AddColumn<string>(
                name: "url",
                table: "hair_length_reference_images",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }
    }
}
