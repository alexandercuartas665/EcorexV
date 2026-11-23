using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormacionEvaluacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "evaluacion_activa",
                table: "cursos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "intentos_max",
                table: "cursos",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "puntaje_minimo",
                table: "cursos",
                type: "integer",
                nullable: false,
                defaultValue: 70);

            migrationBuilder.AddColumn<bool>(
                name: "aprobado",
                table: "curso_progresos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "bloqueado",
                table: "curso_progresos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "certificado_codigo",
                table: "curso_progresos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "desbloqueado_por_tenant_user_id",
                table: "curso_progresos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fecha_aprobacion",
                table: "curso_progresos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fecha_desbloqueo",
                table: "curso_progresos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "intentos",
                table: "curso_progresos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "intentos_perdonados",
                table: "curso_progresos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "mejor_nota",
                table: "curso_progresos",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "curso_intentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    curso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    puntaje = table.Column<int>(type: "integer", nullable: false),
                    correctas = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    aprobado = table.Column<bool>(type: "boolean", nullable: false),
                    respuestas_json = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_curso_intentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_curso_intentos_cursos_curso_id",
                        column: x => x.curso_id,
                        principalTable: "cursos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "curso_preguntas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    curso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    enunciado = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    opciones_json = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    indice_correcto = table.Column<int>(type: "integer", nullable: false),
                    retroalimentacion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_curso_preguntas", x => x.id);
                    table.ForeignKey(
                        name: "fk_curso_preguntas_cursos_curso_id",
                        column: x => x.curso_id,
                        principalTable: "cursos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_curso_progresos_certificado_codigo",
                table: "curso_progresos",
                column: "certificado_codigo",
                unique: true,
                filter: "certificado_codigo IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_curso_intentos_curso_id",
                table: "curso_intentos",
                column: "curso_id");

            migrationBuilder.CreateIndex(
                name: "ix_curso_intentos_tenant_id_curso_id_tenant_user_id",
                table: "curso_intentos",
                columns: new[] { "tenant_id", "curso_id", "tenant_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_curso_preguntas_curso_id_orden",
                table: "curso_preguntas",
                columns: new[] { "curso_id", "orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "curso_intentos");

            migrationBuilder.DropTable(
                name: "curso_preguntas");

            migrationBuilder.DropIndex(
                name: "ix_curso_progresos_certificado_codigo",
                table: "curso_progresos");

            migrationBuilder.DropColumn(
                name: "evaluacion_activa",
                table: "cursos");

            migrationBuilder.DropColumn(
                name: "intentos_max",
                table: "cursos");

            migrationBuilder.DropColumn(
                name: "puntaje_minimo",
                table: "cursos");

            migrationBuilder.DropColumn(
                name: "aprobado",
                table: "curso_progresos");

            migrationBuilder.DropColumn(
                name: "bloqueado",
                table: "curso_progresos");

            migrationBuilder.DropColumn(
                name: "certificado_codigo",
                table: "curso_progresos");

            migrationBuilder.DropColumn(
                name: "desbloqueado_por_tenant_user_id",
                table: "curso_progresos");

            migrationBuilder.DropColumn(
                name: "fecha_aprobacion",
                table: "curso_progresos");

            migrationBuilder.DropColumn(
                name: "fecha_desbloqueo",
                table: "curso_progresos");

            migrationBuilder.DropColumn(
                name: "intentos",
                table: "curso_progresos");

            migrationBuilder.DropColumn(
                name: "intentos_perdonados",
                table: "curso_progresos");

            migrationBuilder.DropColumn(
                name: "mejor_nota",
                table: "curso_progresos");
        }
    }
}
