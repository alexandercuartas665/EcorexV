using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConciliacionCustomFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conciliacion_field_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    label = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    options_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion_field_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conciliacion_field_values",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    renglon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conciliacion_field_values", x => x.id);
                    table.ForeignKey(
                        name: "fk_conciliacion_field_values_conciliacion_dian_renglones_rengl",
                        column: x => x.renglon_id,
                        principalTable: "conciliacion_dian_renglones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conciliacion_field_values_conciliacion_field_definitions_fi",
                        column: x => x.field_definition_id,
                        principalTable: "conciliacion_field_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_field_definitions_tenant_id_field_key",
                table: "conciliacion_field_definitions",
                columns: new[] { "tenant_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_field_values_field_definition_id",
                table: "conciliacion_field_values",
                column: "field_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_field_values_renglon_id",
                table: "conciliacion_field_values",
                column: "renglon_id");

            migrationBuilder.CreateIndex(
                name: "ix_conciliacion_field_values_tenant_id_renglon_id_field_defini",
                table: "conciliacion_field_values",
                columns: new[] { "tenant_id", "renglon_id", "field_definition_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conciliacion_field_values");

            migrationBuilder.DropTable(
                name: "conciliacion_field_definitions");
        }
    }
}
