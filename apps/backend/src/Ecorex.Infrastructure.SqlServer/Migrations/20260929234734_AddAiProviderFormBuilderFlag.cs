using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddAiProviderFormBuilderFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IDEMPOTENTE (ver la migracion PG homologa): SQL Server no tiene ADD COLUMN IF NOT EXISTS, asi que
            // se guarda con sys.columns. Crea la columna si falta; si ya existe (BD que aplico la migracion vieja
            // 20260929133039), no hace nada -> el deploy no choca con "column already exists".
            migrationBuilder.Sql(
                "IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'ai_provider_configs') AND name = 'use_for_form_builder') " +
                "ALTER TABLE ai_provider_configs ADD use_for_form_builder bit NOT NULL CONSTRAINT DF_ai_provider_configs_use_for_form_builder DEFAULT 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'ai_provider_configs') AND name = 'use_for_form_builder') " +
                "BEGIN " +
                "IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_ai_provider_configs_use_for_form_builder') " +
                "ALTER TABLE ai_provider_configs DROP CONSTRAINT DF_ai_provider_configs_use_for_form_builder; " +
                "ALTER TABLE ai_provider_configs DROP COLUMN use_for_form_builder; " +
                "END");
        }
    }
}
