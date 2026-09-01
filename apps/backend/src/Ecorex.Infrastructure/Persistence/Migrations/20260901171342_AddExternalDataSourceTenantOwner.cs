using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalDataSourceTenantOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allow_write",
                table: "external_data_sources",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_tenant_id",
                table: "external_data_sources",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_data_sources_owner_tenant_id",
                table: "external_data_sources",
                column: "owner_tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_external_data_sources_owner_tenant_id",
                table: "external_data_sources");

            migrationBuilder.DropColumn(
                name: "allow_write",
                table: "external_data_sources");

            migrationBuilder.DropColumn(
                name: "owner_tenant_id",
                table: "external_data_sources");
        }
    }
}
