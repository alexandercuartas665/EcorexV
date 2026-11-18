using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
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
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "media_reader_prompt",
                table: "ai_agents",
                type: "nvarchar(max)",
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
            migrationBuilder.Sql(
                "UPDATE a SET media_reader_prompt = N'" + legacyReader.Replace("'", "''") + "' FROM ai_agents a " +
                "WHERE a.media_reader_prompt IS NULL AND (a.system_prompt LIKE '%VALOR[_]TOTAL[_]COP%' OR a.system_prompt LIKE '%CONSUMO[_]KWH%' OR a.system_prompt LIKE '%TIPO[_]IMAGEN%' OR a.system_prompt LIKE '%FOTO[_]TECHO[_]AREA%' OR a.system_prompt LIKE '%PLACA[_]MOTOBOMBA%' " +
                "OR EXISTS (SELECT 1 FROM ai_agent_cache_fields f WHERE f.agent_id = a.id AND (CONCAT(f.description, ' ', f.label, ' ', f.field_key) LIKE '%VALOR[_]TOTAL[_]COP%' OR CONCAT(f.description, ' ', f.label, ' ', f.field_key) LIKE '%CONSUMO[_]KWH%' OR CONCAT(f.description, ' ', f.label, ' ', f.field_key) LIKE '%TIPO[_]IMAGEN%' OR CONCAT(f.description, ' ', f.label, ' ', f.field_key) LIKE '%FOTO[_]TECHO[_]AREA%' OR CONCAT(f.description, ' ', f.label, ' ', f.field_key) LIKE '%PLACA[_]MOTOBOMBA%')) " +
                "OR EXISTS (SELECT 1 FROM ai_agent_prompts p WHERE p.agent_id = a.id AND (p.body LIKE '%VALOR[_]TOTAL[_]COP%' OR p.body LIKE '%CONSUMO[_]KWH%' OR p.body LIKE '%TIPO[_]IMAGEN%' OR p.body LIKE '%FOTO[_]TECHO[_]AREA%' OR p.body LIKE '%PLACA[_]MOTOBOMBA%')));");
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
