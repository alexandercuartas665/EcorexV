using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailBrandGalleryStyles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "alt",
                table: "template_assets",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "template_assets",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_tenant_user_id",
                table: "template_assets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "height",
                table: "template_assets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "template_assets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "stored_file_id",
                table: "template_assets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "width",
                table: "template_assets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estilo",
                table: "email_templates",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "email_brands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    logo_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    color_primario = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    color_acento = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    estilo_por_defecto = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    sitio_web = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    pie_texto = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_brands", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_brands_tenant_id",
                table: "email_brands",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_brands");

            migrationBuilder.DropColumn(
                name: "alt",
                table: "template_assets");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "template_assets");

            migrationBuilder.DropColumn(
                name: "deleted_by_tenant_user_id",
                table: "template_assets");

            migrationBuilder.DropColumn(
                name: "height",
                table: "template_assets");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "template_assets");

            migrationBuilder.DropColumn(
                name: "stored_file_id",
                table: "template_assets");

            migrationBuilder.DropColumn(
                name: "width",
                table: "template_assets");

            migrationBuilder.DropColumn(
                name: "estilo",
                table: "email_templates");
        }
    }
}
