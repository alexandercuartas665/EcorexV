using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentMediaReaderAndTranscriber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "transcription_model",
                table: "ai_provider_configs",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "media_reader_prompt",
                table: "ai_agents",
                type: "text",
                nullable: true);

            // Datos (ADR-0174): hasta v0.16.334 TODOS los agentes leian imagenes/PDF con una plantilla fija de EPRING.
            // Ahora el lector por defecto es generico; a los agentes que dependen de esos campos (prompt base, datos
            // cache o prompts enrutados que los nombran: en prod, "Agente Comercial" de EPRING) se les copia la
            // plantilla como su prompt de lector para que NO cambien de comportamiento. Idempotente (solo si es null).
            var legacyReader = string.Join("\n", new[]
            {
                "Eres un extractor. Miras UNA imagen o documento que envio un cliente por WhatsApp y devuelves",
                "SOLO un bloque de texto plano con estos campos (una linea por campo). Si un campo no aplica o no se lee, pon",
                "NO_LEGIBLE. No agregues nada mas, sin markdown.",
                "TIPO_IMAGEN: (FACTURA_ENERGIA | FOTO_TECHO_AREA | PLACA_MOTOBOMBA | OTRO)",
                "COMERCIALIZADORA:",
                "VALOR_TOTAL_COP: (solo digitos, el valor total a pagar del mes)",
                "CONSUMO_KWH: (solo digitos)",
                "ESTRATO:",
                "PERIODO_FACTURADO:",
                "CIUDAD:",
                "TIPO_TECHO: (teja | losa | metalico | otro, solo si es FOTO_TECHO_AREA)",
                "MOTOBOMBA: (MARCA/MODELO/POTENCIA_HP/VOLTAJE/FASES/AMPERAJE, solo si es PLACA_MOTOBOMBA)"
            });
            const string pgMatch = "(VALOR_TOTAL_COP|CONSUMO_KWH|TIPO_IMAGEN|FOTO_TECHO_AREA|PLACA_MOTOBOMBA)";
            migrationBuilder.Sql(
                "UPDATE ai_agents a SET media_reader_prompt = '" + legacyReader.Replace("'", "''") + "' " +
                "WHERE a.media_reader_prompt IS NULL AND (" +
                "a.system_prompt ~* '" + pgMatch + "' " +
                "OR EXISTS (SELECT 1 FROM ai_agent_cache_fields f WHERE f.agent_id = a.id AND " +
                "(coalesce(f.description, '') || ' ' || coalesce(f.label, '') || ' ' || coalesce(f.field_key, '')) ~* '" + pgMatch + "') " +
                "OR EXISTS (SELECT 1 FROM ai_agent_prompts p WHERE p.agent_id = a.id AND coalesce(p.body, '') ~* '" + pgMatch + "'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "transcription_model",
                table: "ai_provider_configs");

            migrationBuilder.DropColumn(
                name: "media_reader_prompt",
                table: "ai_agents");
        }
    }
}
