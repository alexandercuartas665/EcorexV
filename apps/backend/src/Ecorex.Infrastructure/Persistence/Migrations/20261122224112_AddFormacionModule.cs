using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormacionModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cursos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    categoria = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    portada_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    publicado = table.Column<bool>(type: "boolean", nullable: false),
                    duracion_minutos = table.Column<int>(type: "integer", nullable: true),
                    created_by_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cursos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "curso_asignaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    curso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destino = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_limite = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_curso_asignaciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_curso_asignaciones_cursos_curso_id",
                        column: x => x.curso_id,
                        principalTable: "cursos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "curso_modulos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    curso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_curso_modulos", x => x.id);
                    table.ForeignKey(
                        name: "fk_curso_modulos_cursos_curso_id",
                        column: x => x.curso_id,
                        principalTable: "cursos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "curso_progresos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    curso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_completado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lecciones_vistas = table.Column<int>(type: "integer", nullable: false),
                    porcentaje = table.Column<int>(type: "integer", nullable: false),
                    ultima_leccion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_curso_progresos", x => x.id);
                    table.ForeignKey(
                        name: "fk_curso_progresos_cursos_curso_id",
                        column: x => x.curso_id,
                        principalTable: "cursos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "curso_lecciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    modulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    curso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    archivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    archivo_nombre = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    archivo_mime = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    archivo_bytes = table.Column<long>(type: "bigint", nullable: true),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    contenido = table.Column<string>(type: "text", nullable: true),
                    wizard_root_prefix = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    wizard_entry_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    duracion_minutos = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_curso_lecciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_curso_lecciones_curso_modulos_modulo_id",
                        column: x => x.modulo_id,
                        principalTable: "curso_modulos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "curso_leccion_vistas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leccion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    curso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vista_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_curso_leccion_vistas", x => x.id);
                    table.ForeignKey(
                        name: "fk_curso_leccion_vistas_curso_lecciones_leccion_id",
                        column: x => x.leccion_id,
                        principalTable: "curso_lecciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_curso_asignaciones_curso_id",
                table: "curso_asignaciones",
                column: "curso_id");

            migrationBuilder.CreateIndex(
                name: "ix_curso_asignaciones_tenant_id_curso_id",
                table: "curso_asignaciones",
                columns: new[] { "tenant_id", "curso_id" });

            migrationBuilder.CreateIndex(
                name: "ix_curso_asignaciones_tenant_id_tenant_user_id",
                table: "curso_asignaciones",
                columns: new[] { "tenant_id", "tenant_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_curso_leccion_vistas_leccion_id",
                table: "curso_leccion_vistas",
                column: "leccion_id");

            migrationBuilder.CreateIndex(
                name: "ix_curso_leccion_vistas_tenant_id_curso_id_tenant_user_id",
                table: "curso_leccion_vistas",
                columns: new[] { "tenant_id", "curso_id", "tenant_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_curso_leccion_vistas_tenant_id_leccion_id_tenant_user_id",
                table: "curso_leccion_vistas",
                columns: new[] { "tenant_id", "leccion_id", "tenant_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_curso_lecciones_modulo_id_orden",
                table: "curso_lecciones",
                columns: new[] { "modulo_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_curso_lecciones_tenant_id_curso_id",
                table: "curso_lecciones",
                columns: new[] { "tenant_id", "curso_id" });

            migrationBuilder.CreateIndex(
                name: "ix_curso_modulos_curso_id_orden",
                table: "curso_modulos",
                columns: new[] { "curso_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_curso_progresos_curso_id",
                table: "curso_progresos",
                column: "curso_id");

            migrationBuilder.CreateIndex(
                name: "ix_curso_progresos_tenant_id_curso_id_tenant_user_id",
                table: "curso_progresos",
                columns: new[] { "tenant_id", "curso_id", "tenant_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_curso_progresos_tenant_id_tenant_user_id",
                table: "curso_progresos",
                columns: new[] { "tenant_id", "tenant_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cursos_tenant_id_publicado",
                table: "cursos",
                columns: new[] { "tenant_id", "publicado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "curso_asignaciones");

            migrationBuilder.DropTable(
                name: "curso_leccion_vistas");

            migrationBuilder.DropTable(
                name: "curso_progresos");

            migrationBuilder.DropTable(
                name: "curso_lecciones");

            migrationBuilder.DropTable(
                name: "curso_modulos");

            migrationBuilder.DropTable(
                name: "cursos");
        }
    }
}
