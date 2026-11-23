using System.Text.Json;
using Ecorex.Application.Integraciones.Cerebro;
using Ecorex.Application.Tenancy;

namespace Ecorex.Application.Mcp;

/// <summary>
/// GUIA de los MCP como cerebro de plataforma (ADR-0204): la herramienta guia_ecorex sirve a CUALQUIER IA conectada
/// con token mcp:tableros el manual de uso (protocolo, reglas, trampas y recetas) publicado por la plataforma como
/// cerebro plantilla marcado "guia". Solo lectura y SIN gate de modulo: leer el manual no expone datos del tenant.
/// </summary>
public sealed partial class TableroMcpToolset
{
    private const string GuideToolName = "guia_ecorex";

    private static readonly AiToolSpec GuideSpec = new(GuideToolName,
        "Manual de uso de este MCP: protocolo, reglas, trampas conocidas y recetas paso a paso (contenedores, conectores, " +
        "reportes, flujos, formularios, drones, agentes, clasificacion IA). Llamala antes de construir algo nuevo. Sin " +
        "argumentos devuelve la nota de entrada y los titulos de todas las notas; 'nota' = una nota completa por titulo; " +
        "'buscar' = notas que contienen el texto (por ejemplo, el texto de un error), maximo 20.",
        """{"type":"object","properties":{"nota":{"type":"string","description":"Titulo de la nota (ver la lista sin argumentos)"},"buscar":{"type":"string","description":"Texto a buscar en titulos y contenido"}},"additionalProperties":false}""");

    private McpGuideService? _guide;

    private McpGuideService Guide => _guide ??= new McpGuideService(_db);

    private async Task<McpToolResult> GuideAsync(JsonElement args, CancellationToken ct)
    {
        var nota = Str(args, "nota");
        var buscar = Str(args, "buscar");
        if (nota is not null && buscar is not null) { return Err("Usa 'nota' o 'buscar', no los dos a la vez."); }

        if (nota is not null)
        {
            var (note, error) = await Guide.NoteAsync(nota, ct);
            if (note is null) { return Err(error ?? "No encontre esa nota."); }
            return Ok(new { titulo = note.Title, contenido = note.Body, enlaces = note.Links });
        }

        if (buscar is not null)
        {
            var (hits, error) = await Guide.SearchAsync(buscar, McpGuideService.MaxHits, ct);
            if (error is not null) { return Err(error); }
            return Ok(new
            {
                buscar,
                total = hits.Count,
                resultados = hits.Select(h => new { titulo = h.Title, fragmento = h.Snippet }),
                siguiente = hits.Count > 0 ? "Abre la nota con guia_ecorex(nota=<titulo>)." : "Sin resultados: prueba con otras palabras o revisa la lista de titulos."
            });
        }

        var (index, err) = await Guide.IndexAsync(ct);
        if (index is null) { return Err(err ?? McpGuideService.NotPublishedMessage); }
        return Ok(new
        {
            guia = index.GuideName,
            version = index.Version,
            entrada = index.Entry is null ? null : new { titulo = index.Entry.Title, contenido = index.Entry.Body },
            notas = index.Titles,
            siguiente = "Lee la receta que corresponda con guia_ecorex(nota=<titulo>); ante un error, guia_ecorex(buscar=<texto del error>)."
        });
    }
}
