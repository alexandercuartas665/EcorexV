using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class ConvertModularGeoFieldTypes : Migration
    {
        // Convierte los campos geograficos de sistema del Directorio Modular (secciones "mod_") de
        // Select generico a los tipos tipados Pais/Departamento/Ciudad, para tenants YA sembrados
        // (el seed nuevo ya los crea tipados). field_type se guarda como texto (nombre del enum).
        // Idempotente: solo toca los que siguen en 'Select'. No afecta al Clasico (ficha_key LIKE 'mod%').

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE tercero_field_definitions SET field_type = 'Pais' " +
                "WHERE ficha_key LIKE 'mod%' AND field_key IN ('pais','pais_rut') AND field_type = 'Select';");
            migrationBuilder.Sql(
                "UPDATE tercero_field_definitions SET field_type = 'Departamento' " +
                "WHERE ficha_key LIKE 'mod%' AND field_key = 'departamento' AND field_type = 'Select';");
            migrationBuilder.Sql(
                "UPDATE tercero_field_definitions SET field_type = 'Ciudad' " +
                "WHERE ficha_key LIKE 'mod%' AND field_key IN ('ciudad','ciudad_rut') AND field_type = 'Select';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE tercero_field_definitions SET field_type = 'Select' " +
                "WHERE ficha_key LIKE 'mod%' AND field_key IN ('pais','pais_rut','departamento','ciudad','ciudad_rut') " +
                "AND field_type IN ('Pais','Departamento','Ciudad');");
        }
    }
}
