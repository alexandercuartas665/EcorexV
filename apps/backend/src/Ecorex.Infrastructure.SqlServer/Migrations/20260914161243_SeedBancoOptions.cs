using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class SeedBancoOptions : Migration
    {
        // Siembra la lista de bancos de Colombia en el campo "banco" (Select, seccion mod_proveedor) para
        // tenants YA sembrados que aun lo tienen sin opciones (el seed nuevo ya las trae). Idempotente:
        // solo toca los que estan vacios; no pisa listas ya personalizadas por el tenant.
        private const string Bancos =
            "Bancolombia\nBanco de Bogota\nDavivienda\nBBVA Colombia\nBanco de Occidente\n" +
            "Banco Popular\nBanco Caja Social\nBanco AV Villas\nBanco Agrario de Colombia\n" +
            "Scotiabank Colpatria\nItau\nBanco Falabella\nBanco Pichincha\nBanco GNB Sudameris\n" +
            "Banco Serfinanza\nBanco Finandina\nBancoomeva\nBanco W\nBancamia\n" +
            "Banco Mundo Mujer\nColtefinanciera\nConfiar\nLulo Bank\nNu\nNequi\nDaviplata";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE tercero_field_definitions SET options = '" + Bancos + "' " +
                "WHERE ficha_key LIKE 'mod%' AND field_key = 'banco' AND field_type = 'Select' " +
                "AND (options IS NULL OR options = '');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Solo revierte lo que sembro esta migracion (no toca listas editadas por el tenant).
            migrationBuilder.Sql(
                "UPDATE tercero_field_definitions SET options = NULL " +
                "WHERE ficha_key LIKE 'mod%' AND field_key = 'banco' AND options = '" + Bancos + "';");
        }
    }
}
