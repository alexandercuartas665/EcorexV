using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormSignatureTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_signature_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    row_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    column_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    require_name = table.Column<bool>(type: "boolean", nullable: false),
                    signer_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    target_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    stored_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_signature_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_signature_tokens_form_responses_response_id",
                        column: x => x.response_id,
                        principalTable: "form_responses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_signature_tokens_response_id",
                table: "form_signature_tokens",
                column: "response_id");

            migrationBuilder.CreateIndex(
                name: "ix_form_signature_tokens_tenant_id_response_id_field_code_row_",
                table: "form_signature_tokens",
                columns: new[] { "tenant_id", "response_id", "field_code", "row_id", "column_id" });

            migrationBuilder.CreateIndex(
                name: "ix_form_signature_tokens_tenant_id_token_hash",
                table: "form_signature_tokens",
                columns: new[] { "tenant_id", "token_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_signature_tokens_token_hash",
                table: "form_signature_tokens",
                column: "token_hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_signature_tokens");
        }
    }
}
