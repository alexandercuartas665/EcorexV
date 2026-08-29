using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Ecorex.Application.Forms.Calc;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Forms;

/// <summary>
/// Rellena una plantilla de impresion (<c>QuoteTemplate</c>, HTML con marcadores) con los datos de un
/// registro de formulario (<c>FormResponse</c>). Reusa el modulo Plantillas para NO imprimir el
/// formulario en si, sino un documento con formato (ej. una cotizacion al cliente).
///
/// <para>Marcadores soportados (ver <see cref="FormTemplateMerge"/>):</para>
/// <list type="bullet">
/// <item><c>{{empresa}}</c>, <c>{{fecha}}</c>, <c>{{numero}}</c>: sistema (tenant, fecha, numero de registro).</item>
/// <item><c>{{campo.codigo}}</c>: valor de un campo del formulario (con su formato de presentacion).</item>
/// <item><c>{{#tabla.items}} ... {{col.idColumna}} ... {{fila}} ... {{/tabla.items}}</c>: bloque que se
/// repite por cada fila del GridDetail <c>items</c>.</item>
/// </list>
/// Corre SIN contexto de sesion (lo invoca el renderer de PDF por URL): ignora el filtro por tenant y
/// acota a mano por el TenantId del registro.
/// </summary>
public interface IFormTemplateRenderService
{
    /// <summary>HTML de la plantilla indicada (o la predeterminada del tenant) rellenada con el
    /// registro. Null si el registro no existe.</summary>
    Task<string?> RenderHtmlAsync(Guid responseId, Guid? templateId = null, CancellationToken cancellationToken = default);
}

public sealed class FormTemplateRenderService : IFormTemplateRenderService
{
    private readonly IApplicationDbContext _db;

    public FormTemplateRenderService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<string?> RenderHtmlAsync(Guid responseId, Guid? templateId = null, CancellationToken cancellationToken = default)
    {
        var response = await _db.FormResponses.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);
        if (response is null) { return null; }

        var tenantId = response.TenantId;
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        var questions = await _db.FormQuestions.IgnoreQueryFilters()
            .Where(q => q.DefinitionId == response.DefinitionId && q.TenantId == tenantId)
            .Select(q => new { q.FieldCode, q.ControlType, q.Format, q.OptionsJson })
            .ToListAsync(cancellationToken);

        var template = templateId is Guid tid
            ? await _db.QuoteTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tid && t.TenantId == tenantId, cancellationToken)
            : null;
        template ??= await _db.QuoteTemplates.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (template is null)
        {
            return "<html><body style='font-family:sans-serif;padding:40px;color:#555'>El tenant aun no tiene una plantilla de impresion configurada.</body></html>";
        }

        var fieldFormat = questions
            .Where(q => !string.IsNullOrWhiteSpace(q.Format))
            .ToDictionary(q => q.FieldCode, q => q.Format, StringComparer.OrdinalIgnoreCase);
        var gridOptions = questions
            .Where(q => q.ControlType == FormControlType.GridDetail)
            .ToDictionary(q => q.FieldCode, q => q.OptionsJson, StringComparer.OrdinalIgnoreCase);
        // Config de impresion de los campos Canvas (croquis): options_json trae printHeader/printCounterLabel.
        var canvasOptions = questions
            .Where(q => q.ControlType == FormControlType.Canvas)
            .ToDictionary(q => q.FieldCode, q => q.OptionsJson, StringComparer.OrdinalIgnoreCase);

        var fecha = (response.TransactionDate ?? response.SubmittedAt ?? response.CreatedAt).ToLocalTime();
        return FormTemplateMerge.Render(
            template.HtmlContent ?? string.Empty, response.Data, fieldFormat, gridOptions, canvasOptions,
            tenant?.Name ?? string.Empty, fecha, response.RecordNumber ?? response.Reference ?? string.Empty);
    }
}

/// <summary>
/// Merge PURO (sin BD) de una plantilla HTML con los datos de un registro. Aislado para poder testear
/// los marcadores sin base de datos. El orden importa: primero los bloques de tabla (para no pisar los
/// {{col.*}} con {{campo.*}}), luego los campos, luego el sistema.
/// </summary>
public static class FormTemplateMerge
{
    public static string Render(
        string templateHtml,
        string? responseDataJson,
        IReadOnlyDictionary<string, string?> fieldFormat,
        IReadOnlyDictionary<string, string?> gridOptions,
        IReadOnlyDictionary<string, string?> canvasOptions,
        string empresa,
        DateTimeOffset fecha,
        string numero)
    {
        var values = ParseResponseData(responseDataJson);
        var html = RenderGridBlocks(templateHtml, values, gridOptions);

        html = Regex.Replace(html, @"\{\{\s*campo\.([a-zA-Z0-9_]+)\s*\}\}", m =>
        {
            var code = m.Groups[1].Value;
            return values.TryGetValue(code, out var raw)
                ? EmitField(fieldFormat.GetValueOrDefault(code), raw, canvasOptions.GetValueOrDefault(code), values, fieldFormat, numero, fecha)
                : string.Empty;
        });

        // Codigo de barras: {{barcode:numero}} o {{barcode:campo.codigo}} -> SVG Code39 inline.
        html = ResolveBarcodes(html, values, numero);

        var sb = new StringBuilder(html);
        sb.Replace("{{empresa}}", Esc(empresa));
        sb.Replace("{{numero}}", Esc(numero));
        // Fechas: {{fecha}} (dd/MM/yyyy), {{fechahora}} (dd/MM/yyyy HH:mm del registro) e {{impreso}}
        // (dd/MM/yyyy HH:mm del momento de impresion). Texto plano.
        return ResolveDateTokens(sb.ToString(), fecha);
    }

    private static string RenderGridBlocks(string html, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string?> gridOptions)
    {
        return Regex.Replace(html, @"\{\{\s*#tabla\.([a-zA-Z0-9_]+)\s*\}\}(.*?)\{\{\s*/tabla\.\1\s*\}\}", m =>
        {
            var code = m.Groups[1].Value;
            var inner = m.Groups[2].Value;
            if (!values.TryGetValue(code, out var raw) || string.IsNullOrWhiteSpace(raw)) { return string.Empty; }

            var rows = ParseGridRows(raw);
            if (rows.Count == 0) { return string.Empty; }

            var cols = gridOptions.TryGetValue(code, out var opts)
                ? FormGridCalculator.ParseColumns(opts)
                : (IReadOnlyList<FormGridColumn>)System.Array.Empty<FormGridColumn>();
            var colFormat = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in cols) { colFormat[col.Id] = col.Format; }

            // CAP 3 (ADR-0081): si la plantilla usa un sub-bloque {{#grupo}} ... {{/grupo}}, se renderiza
            // AGRUPADO por la columna GroupRender (o, si no hay, como un solo grupo). Sin {{#grupo}}, plano.
            if (Regex.IsMatch(inner, @"\{\{\s*#grupo\s*\}\}", RegexOptions.Singleline))
            {
                return RenderGroupedGrid(inner, rows, cols, colFormat);
            }

            var outSb = new StringBuilder();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var chunk = Regex.Replace(inner, @"\{\{\s*col\.([a-zA-Z0-9_]+)\s*\}\}", cm =>
                {
                    var cell = row.TryGetValue(cm.Groups[1].Value, out var cv) ? cv : null;
                    return Esc(FormatCell(colFormat.GetValueOrDefault(cm.Groups[1].Value), cell));
                });
                chunk = chunk.Replace("{{fila}}", (i + 1).ToString(CultureInfo.InvariantCulture));
                outSb.Append(chunk);
            }
            return outSb.ToString();
        }, RegexOptions.Singleline);
    }

    /// <summary>
    /// CAP 3: render AGRUPADO de un bloque de tabla. La grilla se agrupa por la columna GroupRender (o, si
    /// ninguna la tiene, como un unico grupo). Por cada grupo se emite el sub-bloque {{#grupo}}...{{/grupo}}
    /// una vez, resolviendo: {{grupo}} = etiqueta del grupo; {{#filas}}...{{/filas}} = las filas del grupo
    /// (con {{col.x}} y {{fila}} global 1-based); {{subtotal.<colId>}} = agregado de esa columna en el grupo
    /// (segun su Agg). Lo de fuera de {{#grupo}} (cabecera/pie de tabla) queda tal cual, una sola vez.
    /// </summary>
    private static string RenderGroupedGrid(
        string inner, List<Dictionary<string, string?>> rows, IReadOnlyList<FormGridColumn> cols,
        IReadOnlyDictionary<string, string?> colFormat)
    {
        var groupCol = cols.FirstOrDefault(c => c.GroupRender);
        var aggById = cols.ToDictionary(c => c.Id, c => c.Agg, StringComparer.OrdinalIgnoreCase);

        // Grupos en orden de primera aparicion (clave normalizada, misma semantica que el calculo).
        var order = new List<string>();
        var byKey = new Dictionary<string, List<Dictionary<string, string?>>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = groupCol is null ? string.Empty
                : FormGridCalculator.NormalizeKey(row.TryGetValue(groupCol.Id, out var kv) ? kv : null);
            if (!byKey.TryGetValue(key, out var bucket)) { bucket = new List<Dictionary<string, string?>>(); byKey[key] = bucket; order.Add(key); }
            bucket.Add(row);
        }

        // Se separa el bloque {{#grupo}}...{{/grupo}} del resto: lo de ANTES y DESPUES (envoltura de la
        // tabla: thead, apertura/cierre) se emite UNA sola vez; el template de grupo, una vez por grupo.
        var mGroup = Regex.Match(inner, @"\{\{\s*#grupo\s*\}\}(.*?)\{\{\s*/grupo\s*\}\}", RegexOptions.Singleline);
        if (!mGroup.Success) { return inner; }
        var before = inner.Substring(0, mGroup.Index);
        var after = inner.Substring(mGroup.Index + mGroup.Length);
        var groupTpl = mGroup.Groups[1].Value;

        var fila = 0; // {{fila}} es global (1-based) a lo largo de todos los grupos.
        var outSb = new StringBuilder(before);
        foreach (var key in order)
        {
            var groupRows = byKey[key];
            var label = groupCol is null ? string.Empty : GroupLabel(groupCol, groupRows[0]);
            var tpl = Regex.Replace(groupTpl, @"\{\{\s*#filas\s*\}\}(.*?)\{\{\s*/filas\s*\}\}", fm =>
            {
                var rowTpl = fm.Groups[1].Value;
                var rowsSb = new StringBuilder();
                foreach (var row in groupRows)
                {
                    fila++;
                    var rowChunk = Regex.Replace(rowTpl, @"\{\{\s*col\.([a-zA-Z0-9_]+)\s*\}\}", cm =>
                    {
                        var cell = row.TryGetValue(cm.Groups[1].Value, out var cv) ? cv : null;
                        return Esc(FormatCell(colFormat.GetValueOrDefault(cm.Groups[1].Value), cell));
                    });
                    rowChunk = rowChunk.Replace("{{fila}}", fila.ToString(CultureInfo.InvariantCulture));
                    rowsSb.Append(rowChunk);
                }
                return rowsSb.ToString();
            }, RegexOptions.Singleline);
            // Subtotales del grupo por columna.
            tpl = Regex.Replace(tpl, @"\{\{\s*subtotal\.([a-zA-Z0-9_]+)\s*\}\}", sm =>
            {
                var colId = sm.Groups[1].Value;
                if (!aggById.TryGetValue(colId, out var agg) || agg == Ecorex.Domain.Enums.FormAggregate.None) { return string.Empty; }
                var total = FormGridCalculator.Aggregate(agg, groupRows.Select(r => r.TryGetValue(colId, out var v) ? v : null));
                return Esc(FormatCell(colFormat.GetValueOrDefault(colId), total?.ToString(CultureInfo.InvariantCulture)));
            });
            tpl = tpl.Replace("{{grupo}}", Esc(label));
            outSb.Append(tpl);
        }
        outSb.Append(after);
        return outSb.ToString();
    }

    /// <summary>Etiqueta del grupo: valor de la columna clave, mapeado a label si la columna es una lista.</summary>
    private static string GroupLabel(FormGridColumn groupCol, Dictionary<string, string?> firstRow)
    {
        var raw = firstRow.TryGetValue(groupCol.Id, out var v) ? v : null;
        if (string.IsNullOrWhiteSpace(raw)) { return string.Empty; }
        if (groupCol.IsSelect && groupCol.Options is { } opts)
        {
            var match = opts.FirstOrDefault(o => o.Id == raw);
            if (match is not null) { return match.Label; }
        }
        return raw!;
    }

    /// <summary>Data del registro: { fieldCode: { value, type } } (o valor directo) -> fieldCode -> texto.</summary>
    private static Dictionary<string, string> ParseResponseData(string? json)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) { return dict; }
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return dict; }
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var val = p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("value", out var v)
                    ? ScalarText(v)
                    : ScalarText(p.Value);
                dict[p.Name] = val ?? string.Empty;
            }
        }
        catch (JsonException) { /* data invalida: sin campos */ }
        return dict;
    }

    private static string? ScalarText(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => el.GetRawText(), // arreglos (grids) quedan como JSON crudo, para RenderGridBlocks
    };

    private static List<Dictionary<string, string?>> ParseGridRows(string raw)
    {
        var rows = new List<Dictionary<string, string?>>();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return rows; }
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) { continue; }
                var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in el.EnumerateObject()) { row[p.Name] = ScalarText(p.Value); }
                rows.Add(row);
            }
        }
        catch (JsonException) { /* no es un grid valido */ }
        return rows;
    }

    /// <summary>Formato de presentacion (currency/integer/decimal/percent). Sin formato o si no es numero, tal cual.</summary>
    private static string FormatCell(string? format, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return string.Empty; }
        if (string.IsNullOrWhiteSpace(format)) { return value; }
        var ci = CultureInfo.InvariantCulture;
        if (!decimal.TryParse(value.Replace(" ", "").Replace(",", ""), NumberStyles.Any, ci, out var num)) { return value; }
        return format.Trim().ToLowerInvariant() switch
        {
            "currency" => "$ " + num.ToString("#,##0", ci),
            "percent" => num.ToString("0.##", ci) + " %",
            "integer" => Math.Round(num).ToString("#,##0", ci),
            "decimal" => num.ToString("#,##0.00", ci),
            _ => value,
        };
    }

    private static string Esc(string? s)
        => (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>Marcadores de fecha/hora (texto plano): {{fecha}} (dd/MM/yyyy del registro),
    /// {{fechahora}} (dd/MM/yyyy HH:mm del registro) e {{impreso}} (dd/MM/yyyy HH:mm del momento de
    /// impresion, hora local). Compartido por el merge principal y el cabezote del Canvas.</summary>
    private static string ResolveDateTokens(string html, DateTimeOffset fecha)
    {
        var impreso = DateTimeOffset.Now;
        return html
            .Replace("{{fechahora}}", fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))
            .Replace("{{impreso}}", impreso.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))
            .Replace("{{fecha}}", fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
    }

    /// <summary>Emite el valor de un campo en la plantilla. Los artefactos grafados AUTOCONTENIDOS (ADR Canvas:
    /// un SVG que empieza con '&lt;svg'; o una imagen data-URL 'data:image') se emiten como MARKUP sin escapar
    /// (Chromium los renderiza al generar el PDF); el resto se formatea y se escapa normal para prevenir HTML.</summary>
    private static string EmitField(string? format, string? raw, string? canvasOptionsJson,
        IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string?> fieldFormat, string numero, DateTimeOffset fecha)
    {
        var v = raw?.TrimStart();
        if (!string.IsNullOrEmpty(v))
        {
            // Canvas (croquis, ADR-0080): SVG suelto (legacy) o {"v":1,"pages":[...]} multipagina -> N hojas,
            // una por pagina, con salto de pagina y "{label} X de N". Si el campo tiene printHeader en su
            // options_json, se repite ese cabezote (con sus {{campo.x}} resueltos) arriba de cada hoja.
            if (v.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
                || (v[0] == '{' && FormCanvasHtml.IsCanvasValue(raw)))
            {
                var (header, counter, skipFirst, footer) = ParseCanvasPrint(canvasOptionsJson);
                var resolvedHeader = string.IsNullOrWhiteSpace(header) ? null : ResolveHeaderTokens(header!, values, fieldFormat, numero, fecha);
                var resolvedFooter = string.IsNullOrWhiteSpace(footer) ? null : ResolveHeaderTokens(footer!, values, fieldFormat, numero, fecha);
                return FormCanvasHtml.Render(raw, resolvedHeader, counter, skipFirst, resolvedFooter);
            }
            if (v.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            {
                return "<img src=\"" + raw!.Trim() + "\" style=\"max-width:100%;height:auto\" alt=\"\" />";
            }
        }
        return Esc(FormatCell(format, raw));
    }

    /// <summary>Lee del options_json del campo Canvas el cabezote de impresion (printHeader, HTML con tokens
    /// {{campo.x}}), la etiqueta del contador (printCounterLabel, default "Grafico") y si se OMITE el cabezote
    /// en la primera hoja (printHeaderSkipFirst, default false: cabezote en todas las hojas).</summary>
    private static (string? header, string counter, bool skipFirst, string? footer) ParseCanvasPrint(string? optionsJson)
    {
        string? header = null; var counter = "Grafico"; var skipFirst = false; string? footer = null;
        if (!string.IsNullOrWhiteSpace(optionsJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(optionsJson);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("printHeader", out var h) && h.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var s = h.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) { header = s; }
                    }
                    if (doc.RootElement.TryGetProperty("printCounterLabel", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var s = c.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) { counter = s!.Trim(); }
                    }
                    // Omitir el cabezote en la 1a hoja: bool true, o el numero 1 (compat con "true"/1).
                    if (doc.RootElement.TryGetProperty("printHeaderSkipFirst", out var sf))
                    {
                        skipFirst = sf.ValueKind == System.Text.Json.JsonValueKind.True
                            || (sf.ValueKind == System.Text.Json.JsonValueKind.String && bool.TryParse(sf.GetString(), out var b) && b);
                    }
                    // Pie por hoja (HTML con {{campo.x}}), analogo a printHeader.
                    if (doc.RootElement.TryGetProperty("printFooter", out var f) && f.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var s = f.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) { footer = s; }
                    }
                }
            }
            catch (System.Text.Json.JsonException) { /* config invalida: sin cabezote */ }
        }
        return (header, counter, skipFirst, footer);
    }

    /// <summary>Resuelve los {{campo.x}} del cabezote contra los valores del registro (mismo criterio que los
    /// campos: valor formateado y ESCAPADO) y los {{barcode:...}} (SVG inline). El HTML del cabezote
    /// (etiquetas) es de config y NO se escapa. Recibe <paramref name="numero"/> para {{barcode:numero}}.</summary>
    private static string ResolveHeaderTokens(string template,
        IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string?> fieldFormat, string numero, DateTimeOffset fecha)
    {
        var html = Regex.Replace(template, @"\{\{\s*campo\.([a-zA-Z0-9_]+)\s*\}\}", m =>
        {
            var code = m.Groups[1].Value;
            return values.TryGetValue(code, out var raw) ? Esc(FormatCell(fieldFormat.GetValueOrDefault(code), raw)) : string.Empty;
        });
        // Codigo de barras y fechas ({{fecha}}/{{fechahora}}/{{impreso}}), igual que en el cuerpo,
        // para que el cabezote/pie por hoja los soporte.
        return ResolveDateTokens(ResolveBarcodes(html, values, numero), fecha);
    }

    /// <summary>Resuelve los marcadores de codigo de barras: <c>{{barcode:numero}}</c> (codifica el numero de
    /// registro) y <c>{{barcode:campo.codigo}}</c> (codifica el valor de un campo). Sintaxis opcional de altura:
    /// <c>{{barcode:target:48}}</c> (px, default 44). Emite un SVG Code39 INLINE (sin escapar); vacio si el
    /// valor no tiene caracteres codificables.</summary>
    private static string ResolveBarcodes(string html, IReadOnlyDictionary<string, string> values, string numero)
        => Regex.Replace(html, @"\{\{\s*barcode:\s*(numero|campo\.[a-zA-Z0-9_]+)\s*(?::\s*(\d+)\s*)?\}\}", m =>
        {
            var target = m.Groups[1].Value;
            var data = target == "numero"
                ? numero
                : (values.TryGetValue(target.Substring("campo.".Length), out var v) ? v : string.Empty);
            if (string.IsNullOrWhiteSpace(data)) { return string.Empty; }
            var height = m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var h) ? h : 44;
            return Barcode.Code39Svg(data, height);
        });
}
