using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Ecorex.Application.Scraping;

/// <summary>Resultado tipado del parseo (nunca lanza hacia el servicio).</summary>
/// <param name="Ok">true si el contenido se pudo interpretar.</param>
/// <param name="Error">Motivo si !Ok (JSON invalido, selector CSS invalido...).</param>
/// <param name="ItemCount">Total de items encontrados (puede superar a Rows: la preview esta acotada).</param>
/// <param name="Columns">Encabezados de la preview tabular.</param>
/// <param name="Rows">Preview de items (maximo MaxPreviewItems), alineada con Columns.</param>
public sealed record ScrapeParseResult(
    bool Ok,
    string? Error,
    int ItemCount,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows)
{
    public static ScrapeParseResult Fail(string error) => new(false, error, 0, [], []);
}

/// <summary>
/// Parser PURO (sin IO, testeable por unidad, mismo criterio que CsvTableParser) del
/// contenido descargado por el ejecutor de extraccion (ADR-0025).
/// - Json: cuenta items (array raiz, o la primera propiedad array del objeto raiz, o el
///   objeto mismo como unico item) y arma una preview tabular con las propiedades escalares.
/// - Html: extrae el TEXTO de los nodos que casan con el selector CSS usando AngleSharp
///   (parser puro, sin red ni telemetria). Un selector invalido es un error tipado.
/// Nada del contenido se ejecuta ni se interpreta como script (a diferencia del legacy).
/// </summary>
public static class ScrapeContentParser
{
    public static ScrapeParseResult ParseJson(string body, int maxPreviewItems)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            return ScrapeParseResult.Fail($"La respuesta no es JSON valido: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            var items = root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray().ToList(),
                JsonValueKind.Object => FirstArrayProperty(root) ?? [root.Clone()],
                _ => [root.Clone()]
            };

            // Columnas: union de propiedades escalares de los primeros items; si los items
            // no son objetos (numeros, strings), una unica columna "valor".
            var columns = new List<string>();
            foreach (var item in items.Take(maxPreviewItems))
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                foreach (var property in item.EnumerateObject())
                {
                    if (!columns.Contains(property.Name))
                    {
                        columns.Add(property.Name);
                    }
                }
            }
            if (columns.Count == 0)
            {
                columns.Add("valor");
            }

            var rows = new List<IReadOnlyList<string?>>();
            foreach (var item in items.Take(maxPreviewItems))
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var row = new string?[columns.Count];
                    for (var i = 0; i < columns.Count; i++)
                    {
                        row[i] = item.TryGetProperty(columns[i], out var value) ? Scalar(value) : null;
                    }
                    rows.Add(row);
                }
                else
                {
                    rows.Add([Scalar(item)]);
                }
            }

            return new ScrapeParseResult(true, null, items.Count, columns, rows);
        }
    }

    public static ScrapeParseResult ParseHtml(string body, string? selector, int maxPreviewItems)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return ScrapeParseResult.Fail("Las fuentes HTML requieren un selector CSS.");
        }

        var parser = new HtmlParser();
        var document = parser.ParseDocument(body);

        IHtmlCollection<IElement> matches;
        try
        {
            matches = document.QuerySelectorAll(selector.Trim());
        }
        catch (Exception ex) when (ex is DomException or ArgumentException)
        {
            return ScrapeParseResult.Fail($"Selector CSS invalido: {selector.Trim()}");
        }

        var rows = matches
            .Take(maxPreviewItems)
            .Select(m => (IReadOnlyList<string?>)[CollapseWhitespace(m.TextContent)])
            .ToList();

        return new ScrapeParseResult(true, null, matches.Length, ["texto"], rows);
    }

    /// <summary>
    /// Documento JSON persistible del resultado, SIEMPRE valido (jsonb en PG lo exige) y
    /// recortado a maxBytes quitando filas de la preview (nunca truncando bytes crudos).
    /// </summary>
    public static string BuildResultJson(ScrapeParseResult parse, int maxBytes)
    {
        var rows = parse.Rows.ToList();
        while (true)
        {
            var json = JsonSerializer.Serialize(new
            {
                itemCount = parse.ItemCount,
                columns = parse.Columns,
                previewCount = rows.Count,
                rows
            });
            if (System.Text.Encoding.UTF8.GetByteCount(json) <= maxBytes || rows.Count == 0)
            {
                return json;
            }
            rows.RemoveAt(rows.Count - 1);
        }
    }

    private static List<JsonElement>? FirstArrayProperty(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                return property.Value.EnumerateArray().Select(e => e.Clone()).ToList();
            }
        }
        return null;
    }

    private static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => value.GetRawText() is { Length: > 120 } raw ? raw[..120] + "..." : value.GetRawText()
    };

    private static string CollapseWhitespace(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
