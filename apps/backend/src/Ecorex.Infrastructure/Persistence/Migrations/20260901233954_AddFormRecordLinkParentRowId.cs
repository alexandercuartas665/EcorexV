using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormRecordLinkParentRowId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "parent_row_id",
                table: "form_record_links",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_record_links_parent_response_id_parent_field_code_pare",
                table: "form_record_links",
                columns: new[] { "parent_response_id", "parent_field_code", "parent_row_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_form_record_links_parent_response_id_parent_field_code_pare",
                table: "form_record_links");

            migrationBuilder.DropColumn(
                name: "parent_row_id",
                table: "form_record_links");
        }
    }
}
