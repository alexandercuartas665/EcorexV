using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTerceroVinculo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tercero_vinculos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cargo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    principal = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tercero_vinculos", x => x.id);
                    table.ForeignKey(
                        name: "fk_tercero_vinculos_terceros_organizacion_id",
                        column: x => x.organizacion_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tercero_vinculos_terceros_persona_id",
                        column: x => x.persona_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tercero_vinculos_organizacion_id",
                table: "tercero_vinculos",
                column: "organizacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_tercero_vinculos_persona_id",
                table: "tercero_vinculos",
                column: "persona_id");

            migrationBuilder.CreateIndex(
                name: "ix_tercero_vinculos_tenant_id_organizacion_id",
                table: "tercero_vinculos",
                columns: new[] { "tenant_id", "organizacion_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tercero_vinculos_tenant_id_persona_id",
                table: "tercero_vinculos",
                columns: new[] { "tenant_id", "persona_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tercero_vinculos_tenant_id_persona_id_organizacion_id",
                table: "tercero_vinculos",
                columns: new[] { "tenant_id", "persona_id", "organizacion_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tercero_vinculos");
        }
    }
}
