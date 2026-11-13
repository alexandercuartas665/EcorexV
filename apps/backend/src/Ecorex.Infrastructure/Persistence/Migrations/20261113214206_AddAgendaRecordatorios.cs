using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgendaRecordatorios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "horario_desde_utc",
                table: "agenda_turnos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agenda_recordatorio_reglas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    servicio_ids_json = table.Column<string>(type: "text", nullable: false),
                    anticipacion_minutos = table.Column<int>(type: "integer", nullable: false),
                    hora_desde = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    hora_hasta = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    fuera_de_horario = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    solo_si_no_confirmado = table.Column<bool>(type: "boolean", nullable: false),
                    canal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    whats_app_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    plantilla_id = table.Column<Guid>(type: "uuid", nullable: true),
                    variables_json = table.Column<string>(type: "text", nullable: false),
                    ai_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    voice_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    objetivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("pk_agenda_recordatorio_reglas", x => x.id);
                    table.ForeignKey(
                        name: "fk_agenda_recordatorio_reglas_ai_agents_ai_agent_id",
                        column: x => x.ai_agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agenda_recordatorio_reglas_whats_app_lines_whats_app_line_id",
                        column: x => x.whats_app_line_id,
                        principalTable: "whats_app_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agenda_recordatorio_reglas_whats_app_templates_plantilla_id",
                        column: x => x.plantilla_id,
                        principalTable: "whats_app_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agenda_recordatorio_envios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    regla_id = table.Column<Guid>(type: "uuid", nullable: false),
                    turno_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inicio_turno_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    canal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    programado_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    procesado_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    detalle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    referencia_externa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agenda_recordatorio_envios", x => x.id);
                    table.ForeignKey(
                        name: "fk_agenda_recordatorio_envios_agenda_recordatorio_reglas_regla",
                        column: x => x.regla_id,
                        principalTable: "agenda_recordatorio_reglas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agenda_recordatorio_envios_agenda_turnos_turno_id",
                        column: x => x.turno_id,
                        principalTable: "agenda_turnos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recordatorio_envios_regla_id",
                table: "agenda_recordatorio_envios",
                column: "regla_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recordatorio_envios_tenant_id_procesado_utc",
                table: "agenda_recordatorio_envios",
                columns: new[] { "tenant_id", "procesado_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recordatorio_envios_tenant_id_regla_id_turno_id_inic",
                table: "agenda_recordatorio_envios",
                columns: new[] { "tenant_id", "regla_id", "turno_id", "inicio_turno_utc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recordatorio_envios_turno_id",
                table: "agenda_recordatorio_envios",
                column: "turno_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recordatorio_reglas_ai_agent_id",
                table: "agenda_recordatorio_reglas",
                column: "ai_agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recordatorio_reglas_plantilla_id",
                table: "agenda_recordatorio_reglas",
                column: "plantilla_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recordatorio_reglas_tenant_id_activa",
                table: "agenda_recordatorio_reglas",
                columns: new[] { "tenant_id", "activa" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recordatorio_reglas_whats_app_line_id",
                table: "agenda_recordatorio_reglas",
                column: "whats_app_line_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agenda_recordatorio_envios");

            migrationBuilder.DropTable(
                name: "agenda_recordatorio_reglas");

            migrationBuilder.DropColumn(
                name: "horario_desde_utc",
                table: "agenda_turnos");
        }
    }
}
