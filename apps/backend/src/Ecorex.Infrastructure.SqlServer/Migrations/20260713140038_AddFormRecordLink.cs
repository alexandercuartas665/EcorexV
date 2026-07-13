using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddFormRecordLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "subform_definition_id",
                table: "form_questions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "form_record_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_response_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_field_code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    child_response_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_record_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_record_links_form_responses_child_response_id",
                        column: x => x.child_response_id,
                        principalTable: "form_responses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_form_record_links_form_responses_parent_response_id",
                        column: x => x.parent_response_id,
                        principalTable: "form_responses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_record_links_child_response_id",
                table: "form_record_links",
                column: "child_response_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_record_links_parent_response_id_parent_field_code_child_response_id",
                table: "form_record_links",
                columns: new[] { "parent_response_id", "parent_field_code", "child_response_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_record_links");

            migrationBuilder.DropColumn(
                name: "subform_definition_id",
                table: "form_questions");
        }
    }
}
