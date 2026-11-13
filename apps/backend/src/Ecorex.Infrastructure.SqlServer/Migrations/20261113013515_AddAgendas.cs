using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAgendas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agenda_plantillas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    descripcion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    activa = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agenda_plantillas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agenda_tipos_recurso",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    icono = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    vinculo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    orden = table.Column<int>(type: "int", nullable: false),
                    activo = table.Column<bool>(type: "bit", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agenda_tipos_recurso", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agenda_plantilla_franjas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plantilla_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dia_semana = table.Column<int>(type: "int", nullable: false),
                    hora_inicio = table.Column<TimeOnly>(type: "time", nullable: false),
                    hora_fin = table.Column<TimeOnly>(type: "time", nullable: false),
                    intervalo_minutos = table.Column<int>(type: "int", nullable: false),
                    cupo = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agenda_plantilla_franjas", x => x.id);
                    table.ForeignKey(
                        name: "fk_agenda_plantilla_franjas_agenda_plantillas_plantilla_id",
                        column: x => x.plantilla_id,
                        principalTable: "agenda_plantillas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agenda_recursos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    tipo_recurso_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sede_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    agente_habilitado = table.Column<bool>(type: "bit", nullable: false),
                    activo = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agenda_recursos", x => x.id);
                    table.ForeignKey(
                        name: "fk_agenda_recursos_agenda_tipos_recurso_tipo_recurso_id",
                        column: x => x.tipo_recurso_id,
                        principalTable: "agenda_tipos_recurso",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agenda_recursos_entidades_sede_id",
                        column: x => x.sede_id,
                        principalTable: "entidades",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agenda_recursos_tenant_users_tenant_user_id",
                        column: x => x.tenant_user_id,
                        principalTable: "tenant_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agenda_novedades",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recurso_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    fecha_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_hasta = table.Column<DateOnly>(type: "date", nullable: false),
                    hora_desde = table.Column<TimeOnly>(type: "time", nullable: true),
                    hora_hasta = table.Column<TimeOnly>(type: "time", nullable: true),
                    nota = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agenda_novedades", x => x.id);
                    table.ForeignKey(
                        name: "fk_agenda_novedades_agenda_recursos_recurso_id",
                        column: x => x.recurso_id,
                        principalTable: "agenda_recursos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agenda_recurso_vigencias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recurso_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    vigente_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    plantilla_origen_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    is_deleted = table.Column<bool>(type: "bit", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    deleted_by_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agenda_recurso_vigencias", x => x.id);
                    table.ForeignKey(
                        name: "fk_agenda_recurso_vigencias_agenda_plantillas_plantilla_origen_id",
                        column: x => x.plantilla_origen_id,
                        principalTable: "agenda_plantillas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agenda_recurso_vigencias_agenda_recursos_recurso_id",
                        column: x => x.recurso_id,
                        principalTable: "agenda_recursos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agenda_recurso_franjas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    vigencia_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dia_semana = table.Column<int>(type: "int", nullable: false),
                    hora_inicio = table.Column<TimeOnly>(type: "time", nullable: false),
                    hora_fin = table.Column<TimeOnly>(type: "time", nullable: false),
                    intervalo_minutos = table.Column<int>(type: "int", nullable: false),
                    cupo = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agenda_recurso_franjas", x => x.id);
                    table.ForeignKey(
                        name: "fk_agenda_recurso_franjas_agenda_recurso_vigencias_vigencia_id",
                        column: x => x.vigencia_id,
                        principalTable: "agenda_recurso_vigencias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_novedades_recurso_id",
                table: "agenda_novedades",
                column: "recurso_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_novedades_tenant_id_recurso_id_fecha_desde",
                table: "agenda_novedades",
                columns: new[] { "tenant_id", "recurso_id", "fecha_desde" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_plantilla_franjas_plantilla_id",
                table: "agenda_plantilla_franjas",
                column: "plantilla_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_plantilla_franjas_tenant_id_plantilla_id_dia_semana",
                table: "agenda_plantilla_franjas",
                columns: new[] { "tenant_id", "plantilla_id", "dia_semana" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_plantillas_tenant_id_nombre",
                table: "agenda_plantillas",
                columns: new[] { "tenant_id", "nombre" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recurso_franjas_tenant_id_vigencia_id_dia_semana",
                table: "agenda_recurso_franjas",
                columns: new[] { "tenant_id", "vigencia_id", "dia_semana" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recurso_franjas_vigencia_id",
                table: "agenda_recurso_franjas",
                column: "vigencia_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recurso_vigencias_plantilla_origen_id",
                table: "agenda_recurso_vigencias",
                column: "plantilla_origen_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recurso_vigencias_recurso_id",
                table: "agenda_recurso_vigencias",
                column: "recurso_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recurso_vigencias_tenant_id_recurso_id_vigente_desde",
                table: "agenda_recurso_vigencias",
                columns: new[] { "tenant_id", "recurso_id", "vigente_desde" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recursos_sede_id",
                table: "agenda_recursos",
                column: "sede_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recursos_tenant_id_nombre",
                table: "agenda_recursos",
                columns: new[] { "tenant_id", "nombre" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recursos_tenant_id_tipo_recurso_id",
                table: "agenda_recursos",
                columns: new[] { "tenant_id", "tipo_recurso_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recursos_tenant_user_id",
                table: "agenda_recursos",
                column: "tenant_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_recursos_tipo_recurso_id",
                table: "agenda_recursos",
                column: "tipo_recurso_id");

            migrationBuilder.CreateIndex(
                name: "ix_agenda_tipos_recurso_tenant_id_nombre",
                table: "agenda_tipos_recurso",
                columns: new[] { "tenant_id", "nombre" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agenda_novedades");

            migrationBuilder.DropTable(
                name: "agenda_plantilla_franjas");

            migrationBuilder.DropTable(
                name: "agenda_recurso_franjas");

            migrationBuilder.DropTable(
                name: "agenda_recurso_vigencias");

            migrationBuilder.DropTable(
                name: "agenda_plantillas");

            migrationBuilder.DropTable(
                name: "agenda_recursos");

            migrationBuilder.DropTable(
                name: "agenda_tipos_recurso");
        }
    }
}
