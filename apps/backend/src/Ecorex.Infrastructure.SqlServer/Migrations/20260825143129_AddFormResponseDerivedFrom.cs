using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddFormResponseDerivedFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "derived_from_response_id",
                table: "form_responses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_responses_tenant_id_derived_from_response_id",
                table: "form_responses",
                columns: new[] { "tenant_id", "derived_from_response_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_form_responses_tenant_id_derived_from_response_id",
                table: "form_responses");

            migrationBuilder.DropColumn(
                name: "derived_from_response_id",
                table: "form_responses");
        }
    }
}
