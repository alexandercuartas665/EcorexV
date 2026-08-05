using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalDataConnector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_binding_json",
                table: "report_definitions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "external_data_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    connection_string_encrypted = table.Column<string>(type: "text", nullable: true),
                    is_read_only = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_validated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_data_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_data_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    command_text = table.Column<string>(type: "text", nullable: false),
                    parameters_json = table.Column<string>(type: "jsonb", nullable: true),
                    fields_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_data_sets", x => x.id);
                    table.ForeignKey(
                        name: "fk_external_data_sets_external_data_sources_external_data_sour",
                        column: x => x.external_data_source_id,
                        principalTable: "external_data_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_data_source_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_data_source_grants", x => x.id);
                    table.ForeignKey(
                        name: "fk_external_data_source_grants_external_data_sources_external_",
                        column: x => x.external_data_source_id,
                        principalTable: "external_data_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_data_sets_external_data_source_id",
                table: "external_data_sets",
                column: "external_data_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_data_source_grants_external_data_source_id_tenant_",
                table: "external_data_source_grants",
                columns: new[] { "external_data_source_id", "tenant_id", "rol_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_data_source_grants_tenant_id",
                table: "external_data_source_grants",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_data_sources_is_enabled",
                table: "external_data_sources",
                column: "is_enabled");

            migrationBuilder.CreateIndex(
                name: "ix_external_data_sources_name",
                table: "external_data_sources",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_data_sets");

            migrationBuilder.DropTable(
                name: "external_data_source_grants");

            migrationBuilder.DropTable(
                name: "external_data_sources");

            migrationBuilder.DropColumn(
                name: "external_binding_json",
                table: "report_definitions");
        }
    }
}
