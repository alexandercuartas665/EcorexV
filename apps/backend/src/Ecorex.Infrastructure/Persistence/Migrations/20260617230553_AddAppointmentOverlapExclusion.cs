using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentOverlapExclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_appointments_tenant_id_resource_id_appointment_date_start_t",
                table: "appointments");

            migrationBuilder.AddColumn<int>(
                name: "buffer_minutes",
                table: "resources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "scheduling_mode",
                table: "resources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "buffer_minutes",
                table: "appointments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // ANTI-OVERBOOKING por SOLAPAMIENTO (regla de oro del dominio). Reemplaza al UNIQUE(start_time):
            // ninguna pareja de citas ACTIVAS del mismo (tenant, recurso, fecha) puede cruzar su intervalo
            // [inicio, inicio + duracion + buffer). El rango es medio-abierto '[)' para que dos citas pegadas
            // (3:00-3:30 y 3:30-4:00) NO choquen, pero 3:00-3:45 y 3:30-4:00 SI. btree_gist habilita mezclar
            // igualdad (=) en columnas escalares con solapamiento (&&) de rango en el mismo indice GiST.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql(@"
                ALTER TABLE appointments ADD CONSTRAINT ck_appointments_no_overlap
                EXCLUDE USING gist (
                    tenant_id WITH =,
                    resource_id WITH =,
                    appointment_date WITH =,
                    tsrange(
                        timestamp '2000-01-01' + start_time,
                        timestamp '2000-01-01' + start_time + ((duration_minutes + buffer_minutes) * interval '1 minute'),
                        '[)'
                    ) WITH &&
                )
                WHERE (status NOT IN ('Cancelled', 'Rescheduled'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Quitar primero el constraint (referencia buffer_minutes/duration_minutes). La extension
            // btree_gist se deja instalada por si otros objetos la usan.
            migrationBuilder.Sql("ALTER TABLE appointments DROP CONSTRAINT IF EXISTS ck_appointments_no_overlap;");

            migrationBuilder.DropColumn(
                name: "buffer_minutes",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "scheduling_mode",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "buffer_minutes",
                table: "appointments");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_tenant_id_resource_id_appointment_date_start_t",
                table: "appointments",
                columns: new[] { "tenant_id", "resource_id", "appointment_date", "start_time" },
                unique: true,
                filter: "status NOT IN ('Cancelled', 'Rescheduled')");
        }
    }
}
