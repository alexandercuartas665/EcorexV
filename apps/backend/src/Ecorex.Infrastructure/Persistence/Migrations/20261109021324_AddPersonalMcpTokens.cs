using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalMcpTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_user_id",
                table: "tenant_api_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_api_tokens_tenant_id_tenant_user_id",
                table: "tenant_api_tokens",
                columns: new[] { "tenant_id", "tenant_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tenant_api_tokens_tenant_id_tenant_user_id",
                table: "tenant_api_tokens");

            migrationBuilder.DropColumn(
                name: "tenant_user_id",
                table: "tenant_api_tokens");
        }
    }
}
