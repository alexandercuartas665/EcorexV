using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddDerivedTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "derived_from_container_id",
                table: "data_containers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_containers_derived_from_container_id",
                table: "data_containers",
                column: "derived_from_container_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_containers_tenant_id_derived_from_container_id",
                table: "data_containers",
                columns: new[] { "tenant_id", "derived_from_container_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_data_containers_data_containers_derived_from_container_id",
                table: "data_containers",
                column: "derived_from_container_id",
                principalTable: "data_containers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_data_containers_data_containers_derived_from_container_id",
                table: "data_containers");

            migrationBuilder.DropIndex(
                name: "ix_data_containers_derived_from_container_id",
                table: "data_containers");

            migrationBuilder.DropIndex(
                name: "ix_data_containers_tenant_id_derived_from_container_id",
                table: "data_containers");

            migrationBuilder.DropColumn(
                name: "derived_from_container_id",
                table: "data_containers");
        }
    }
}
