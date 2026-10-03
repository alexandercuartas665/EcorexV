using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiProviderFormBuilderFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IDEMPOTENTE: la rama form-builder-chat traia esta columna en una migracion previa (20260929132844)
            // que quedo aplicada en algunas BD (copias de dev que corrieron esa rama). Al regenerarla al final
            // del merge (este id) EF la reaplicaria y chocaria con "column already exists". Con IF NOT EXISTS la
            // migracion es segura en cualquier estado: crea la columna si falta, o no hace nada si ya esta.
            migrationBuilder.Sql(
                "ALTER TABLE ai_provider_configs ADD COLUMN IF NOT EXISTS use_for_form_builder boolean NOT NULL DEFAULT false;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE ai_provider_configs DROP COLUMN IF EXISTS use_for_form_builder;");
        }
    }
}
