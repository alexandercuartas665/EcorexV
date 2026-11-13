using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgendaServiciosTurnos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agenda_servicios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    duracion_minutos = table.Column<int>(type: "integer", nullable: false),
                    anticipacion_min_minutos = table.Column<int>(type: "integer", nullable: false),
                    anticipacion_max_dias = table.Column<int>(type: "integer", nullable: false),
                    agente_puede_reservar = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_agenda_servicios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agenda_servicio_requisitos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    servicio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    recurso_ids_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agenda_servicio_requisitos", x => x.id);
                    table.ForeignKey(
                        name: "fk_agenda_servicio_requisitos_agenda_servicios_servicio_id",
                        column: x => x.servicio_id,
                        principalTable: "agenda_servicios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agenda_turnos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    servicio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    hora_inicio = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    hora_fin = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    inicio_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fin_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    origen = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cliente_nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    cliente_telefono = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    tercero_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reprogramaciones = table.Column<int>(type: "integer", nullable: false),
                    historial = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("pk_agenda_turnos", x => x.id);
                    table.ForeignKey(
                        name: "fk_agenda_turnos_agenda_servicios_servicio_id",
                        column: x => x.servicio_id,
                        principalTable: "agenda_servicios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agenda_turnos_terceros_tercero_id",
                        column: x => x.tercero_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agenda_turno_recursos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    turno_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recurso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisito = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    hora_inicio = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    hora_fin = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agenda_turno_recursos", x => x.id);
                    table.ForeignKey(
                        name: "fk_agenda_turno_recursos_agenda_recursos_recurso_id",
                        column: x => x.recurso_id,
                        principalTable: "agenda_recursos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agenda_turno_recursos_agenda_turnos_turno_id",
                        column: x => x.turno_id,
                        principalTable: "agenda_turnos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_servicio_requisitos_servicio_id",
                table: "agenda_servicio_requisitos",
                column: "servicio_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_servicio_requisitos_tenant_id_servicio_id_orden",
                table: "agenda_servicio_requisitos",
                columns: new[] { "tenant_id", "servicio_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_servicios_tenant_id_nombre",
                table: "agenda_servicios",
                columns: new[] { "tenant_id", "nombre" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_turno_recursos_recurso_id",
                table: "agenda_turno_recursos",
                column: "recurso_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_turno_recursos_tenant_id_recurso_id_fecha",
                table: "agenda_turno_recursos",
                columns: new[] { "tenant_id", "recurso_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_turno_recursos_turno_id",
                table: "agenda_turno_recursos",
                column: "turno_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_turnos_servicio_id",
                table: "agenda_turnos",
                column: "servicio_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_turnos_tenant_id_fecha",
                table: "agenda_turnos",
                columns: new[] { "tenant_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_turnos_tenant_id_servicio_id_fecha",
                table: "agenda_turnos",
                columns: new[] { "tenant_id", "servicio_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_turnos_tercero_id",
                table: "agenda_turnos",
                column: "tercero_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agenda_servicio_requisitos");

            migrationBuilder.DropTable(
                name: "agenda_turno_recursos");

            migrationBuilder.DropTable(
                name: "agenda_turnos");

            migrationBuilder.DropTable(
                name: "agenda_servicios");
        }
    }
}
