using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormTransactional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "record_number",
                table: "form_responses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "record_status",
                table: "form_responses",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "transaction_date",
                table: "form_responses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "void_reason",
                table: "form_responses",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "voided_at",
                table: "form_responses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "voided_by_tenant_user_id",
                table: "form_responses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "identity_mode",
                table: "form_definitions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "identity_source_field_code",
                table: "form_definitions",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_transactional",
                table: "form_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "sequence_id",
                table: "form_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unique_key_fields_json",
                table: "form_definitions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_form_responses_tenant_id_definition_id_record_number",
                table: "form_responses",
                columns: new[] { "tenant_id", "definition_id", "record_number" },
                unique: true,
                filter: "record_number IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_form_responses_tenant_id_definition_id_record_number",
                table: "form_responses");

            migrationBuilder.DropColumn(
                name: "record_number",
                table: "form_responses");

            migrationBuilder.DropColumn(
                name: "record_status",
                table: "form_responses");

            migrationBuilder.DropColumn(
                name: "transaction_date",
                table: "form_responses");

            migrationBuilder.DropColumn(
                name: "void_reason",
                table: "form_responses");

            migrationBuilder.DropColumn(
                name: "voided_at",
                table: "form_responses");

            migrationBuilder.DropColumn(
                name: "voided_by_tenant_user_id",
                table: "form_responses");

            migrationBuilder.DropColumn(
                name: "identity_mode",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "identity_source_field_code",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "is_transactional",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "sequence_id",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "unique_key_fields_json",
                table: "form_definitions");
        }
    }
}
