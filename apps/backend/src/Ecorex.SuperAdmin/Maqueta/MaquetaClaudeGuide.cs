namespace Ecorex.SuperAdmin.Maqueta;

/// <summary>
/// Ola 4 del cerebro digital (ADR-0183): con el plan Claude Max/Pro del usuario NO hay llave de API para el backend,
/// asi que "chatear con la maqueta" se hace desde Claude Desktop / Claude Code conectado al MCP de maquetas
/// (ADR-0182). Esta guia arma los pasos y los prompts de ejemplo que muestran /conectar-ia y la pestana Cerebro.
/// La conversacion la paga el plan de Claude del usuario; ECOREX solo expone las herramientas.
/// </summary>
public static class MaquetaClaudeGuide
{
    /// <summary>Nombre del servidor MCP sugerido en los clientes (igual al de Conectar IA).</summary>
    public const string ServerName = "ecorex-maquetas";

    /// <summary>Comando de Claude Code con el token como marcador (el token real solo se ve al generarlo).</summary>
    public static string ClaudeCodeCommand(string mcpUrl)
        => $"claude mcp add --transport http {ServerName} {mcpUrl} --header \"Authorization: Bearer <TU_TOKEN_DE_MAQUETAS>\"";

    /// <summary>
    /// Prompts de ejemplo para pensar con la maqueta y su cerebro. Se personalizan con el proyecto y, si los hay, la
    /// nota y la pantalla abiertas. Sin proyecto se usa el marcador "{proyecto}".
    /// </summary>
    public static IReadOnlyList<(string Title, string Prompt)> Prompts(string? slug, string? nota = null, string? pantalla = null)
    {
        var p = string.IsNullOrWhiteSpace(slug) ? "{proyecto}" : slug.Trim();
        var list = new List<(string, string)>
        {
            ("Ponerte al dia",
                $"Usa el MCP ecorex-maquetas. Lee el grafo y las notas del cerebro del proyecto {p} y las solicitudes " +
                "abiertas. Resumeme en que estamos, que decisiones ya tomamos y que quedo suelto o sin responder."),
            ("Pensar una pantalla",
                $"En el proyecto {p}, revisa la pantalla {(string.IsNullOrWhiteSpace(pantalla) ? "<archivo.html>" : pantalla)}: " +
                "que notas del cerebro y que solicitudes la mencionan. Proponme 3 mejoras y, si te digo que si, guardalas " +
                "en una nota nueva enlazada a esa pantalla con [[" + (string.IsNullOrWhiteSpace(pantalla) ? "archivo.html" : pantalla) + "]]."),
            ("Ordenar el cerebro",
                $"En el cerebro de {p}, dime que notas estan sueltas (sin enlaces) y que notas pendientes (enlazadas pero sin " +
                "crear) hay. Sugiere como conectarlas y crea una nota 'Mapa del proyecto' que enlace las principales."),
            ("Guardar una conclusion",
                $"Guarda en el cerebro de {p} una nota 'Decisiones {DateTime.UtcNow:yyyy-MM-dd}' con lo que concluimos en esta " +
                "conversacion, enlazando las pantallas ([[archivo.html]]) y solicitudes ([[T00000]]) que mencionamos.")
        };
        if (!string.IsNullOrWhiteSpace(nota))
        {
            list.Insert(1, ("Profundizar en la nota abierta",
                $"Lee la nota '{nota.Trim()}' del cerebro de {p}, sus enlaces y las notas que la enlazan. Hazme preguntas para " +
                "aterrizarla y, al final, agrega al final de la nota (editar_nota, modo agregar) un resumen de lo que acordemos."));
        }
        return list;
    }
}
