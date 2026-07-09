using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConceptosSedesNotificaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sedes",
                table: "actividad_subcategorias",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "actividad_subcategoria_notificaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subcategoria_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actividad_subcategoria_notificaciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_actividad_subcategoria_notificaciones_actividad_subcategori",
                        column: x => x.subcategoria_id,
                        principalTable: "actividad_subcategorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_actividad_subcategoria_notificaciones_tenant_users_tenant_u",
                        column: x => x.tenant_user_id,
                        principalTable: "tenant_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_notificaciones_subcategoria_id_tenan",
                table: "actividad_subcategoria_notificaciones",
                columns: new[] { "subcategoria_id", "tenant_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_notificaciones_tenant_id_tenant_user",
                table: "actividad_subcategoria_notificaciones",
                columns: new[] { "tenant_id", "tenant_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_notificaciones_tenant_user_id",
                table: "actividad_subcategoria_notificaciones",
                column: "tenant_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "actividad_subcategoria_notificaciones");

            migrationBuilder.DropColumn(
                name: "sedes",
                table: "actividad_subcategorias");
        }
    }
}
