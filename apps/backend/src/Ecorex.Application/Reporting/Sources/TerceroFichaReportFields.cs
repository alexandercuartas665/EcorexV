using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.DataLookups;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.Sources;

/// <summary>
/// Campos DINAMICOS del Directorio para la fuente <c>native:tercero</c> (ADR-0208): uno por campo de ficha
/// configurado por el tenant (<c>TerceroFieldDefinition</c>), con la etiqueta y el tipo de su definicion. El
/// valor vive en <c>terceros.fichas_json</c> (<c>{ seccion: { campo: valor } }</c>); se lee de forma tolerante
/// (escalar, objeto <c>{ value }</c> o lista) y las listas de contenedor / del directorio se resuelven a su
/// etiqueta. Todo sobre el contexto que recibe (efimero, tenant-safe por el filtro global).
/// </summary>
public static class TerceroFichaReportFields
{
    /// <summary>Prefijo de clave: nunca choca con las claves fijas (PascalCase) de la fuente.</summary>
    public const string KeyPrefix = "ficha_";

    /// <summary>Un campo de ficha publicado: donde leerlo y como describirlo.</summary>
    public sealed record FichaField(string Seccion, string Campo, TerceroFieldType FieldType, string? Options, ReportField Field);

    /// <summary>Clave estable <c>ficha_{seccion}_{campo}</c> (minusculas, solo letras/digitos/guion bajo).</summary>
    public static string KeyFor(string seccion, string campo) => KeyPrefix + Slug(seccion) + "_" + Slug(campo);

    /// <summary>
    /// Campos de ficha del tenant activo: primero los DEFINIDOS (etiqueta y tipo de su definicion, en el orden de
    /// fichas y campos) y despues las claves que EXISTEN en los datos sin definicion (texto, etiqueta legible): hay
    /// tenants con datos cargados por importacion en secciones sin campo definido (p.ej. cliente.direccion). Las
    /// etiquetas repetidas entre si o con un campo fijo se desambiguan con el titulo de la ficha.
    /// </summary>
    public static async Task<IReadOnlyList<FichaField>> LoadAsync(
        IApplicationDbContext db, IReadOnlyList<ReportField> fixedFields, IEnumerable<string?> fichasJsons, CancellationToken ct)
    {
        var defs = await db.TerceroFieldDefinitions.AsNoTracking()
            .Select(f => new { f.FichaKey, f.FieldKey, f.Label, f.FieldType, f.Options, f.SortOrder })
            .ToListAsync(ct);
        var fichas = await db.TerceroFichaDefinitions.AsNoTracking()
            .Select(f => new { f.FichaKey, f.Title, f.SortOrder })
            .ToListAsync(ct);
        var titulo = fichas.GroupBy(f => f.FichaKey).ToDictionary(g => g.Key, g => g.First().Title, StringComparer.Ordinal);
        var orden = fichas.GroupBy(f => f.FichaKey).ToDictionary(g => g.Key, g => g.First().SortOrder, StringComparer.Ordinal);

        var candidatos = new List<(string Seccion, string Campo, string Label, TerceroFieldType Type, string? Options)>();
        var definidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in defs
            .OrderBy(d => orden.TryGetValue(d.FichaKey, out var o) ? o : int.MaxValue)
            .ThenBy(d => d.FichaKey, StringComparer.Ordinal)
            .ThenBy(d => d.SortOrder))
        {
            // Separadores y tablas no se publican, pero tampoco vuelven como "clave sin definicion".
            definidos.Add(KeyFor(d.FichaKey, d.FieldKey));
            if (d.FieldType is TerceroFieldType.Separator or TerceroFieldType.Table) { continue; }
            candidatos.Add((d.FichaKey, d.FieldKey, string.IsNullOrWhiteSpace(d.Label) ? Legible(d.FieldKey) : d.Label.Trim(), d.FieldType, d.Options));
        }

        // Claves presentes en los datos sin definicion (solo si alguna fila trae valor).
        var extra = new HashSet<(string Seccion, string Campo)>();
        foreach (var json in fichasJsons)
        {
            if (Parse(json) is not { } doc) { continue; }
            foreach (var (seccion, campos) in doc)
            {
                foreach (var (campo, valor) in campos)
                {
                    if (!definidos.Contains(KeyFor(seccion, campo)) && Scalar(valor) is not null) { extra.Add((seccion, campo)); }
                }
            }
        }
        foreach (var (seccion, campo) in extra
            .OrderBy(e => orden.TryGetValue(e.Seccion, out var o) ? o : int.MaxValue)
            .ThenBy(e => e.Seccion, StringComparer.Ordinal)
            .ThenBy(e => e.Campo, StringComparer.Ordinal))
        {
            candidatos.Add((seccion, campo, Legible(campo), TerceroFieldType.Text, null));
        }
        if (candidatos.Count == 0) { return Array.Empty<FichaField>(); }

        // Nombres ya tomados (los campos se referencian por Key O DisplayName: no puede haber dos iguales).
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in fixedFields) { usados.Add(f.Key); usados.Add(f.DisplayName); }
        var repetidas = candidatos.GroupBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new List<FichaField>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidatos)
        {
            var key = KeyFor(c.Seccion, c.Campo);
            if (!keys.Add(key)) { continue; }

            var label = c.Label;
            if (repetidas.Contains(label) || usados.Contains(label))
            {
                var ficha = titulo.TryGetValue(c.Seccion, out var t) && !string.IsNullOrWhiteSpace(t) ? t.Trim() : Legible(c.Seccion);
                label = $"{label} ({ficha})";
            }
            if (usados.Contains(label)) { label = $"{label} [{key}]"; }
            usados.Add(label);
            usados.Add(key);

            var type = c.Type switch
            {
                TerceroFieldType.Number or TerceroFieldType.Currency => ReportFieldType.Decimal,
                TerceroFieldType.Date => ReportFieldType.Date,
                TerceroFieldType.Checkbox => ReportFieldType.Boolean,
                _ => ReportFieldType.Text,
            };
            var field = new ReportField(key, label, type,
                CanFilter: true,
                CanGroup: c.Type != TerceroFieldType.TextArea,
                CanAggregate: type == ReportFieldType.Decimal);
            result.Add(new FichaField(c.Seccion, c.Campo, c.Type, c.Options, field));
        }
        return result;
    }

    /// <summary>"tipo_de_empresa" -> "Tipo de empresa".</summary>
    public static string Legible(string clave)
    {
        var t = (clave ?? "").Replace('_', ' ').Replace('-', ' ').Trim();
        while (t.Contains("  ", StringComparison.Ordinal)) { t = t.Replace("  ", " ", StringComparison.Ordinal); }
        return t.Length == 0 ? clave ?? "" : char.ToUpperInvariant(t[0]) + t[1..];
    }

    /// <summary>
    /// Valores crudos (texto) de cada campo de ficha por tercero, ya con las listas resueltas a su etiqueta.
    /// Devuelve por tercero un arreglo alineado con <paramref name="fields"/>.
    /// </summary>
    public static async Task<Dictionary<Guid, string?[]>> ExtractAsync(
        IApplicationDbContext db, IReadOnlyList<FichaField> fields, IEnumerable<(Guid Id, string? FichasJson)> terceros,
        IReadOnlyDictionary<Guid, string> terceroNames, CancellationToken ct)
    {
        var result = new Dictionary<Guid, string?[]>();
        foreach (var (id, json) in terceros)
        {
            var vals = new string?[fields.Count];
            var doc = Parse(json);
            if (doc is not null)
            {
                for (var i = 0; i < fields.Count; i++)
                {
                    vals[i] = Read(doc, fields[i].Seccion, fields[i].Campo);
                }
            }
            result[id] = vals;
        }

        await ResolveLabelsAsync(db, fields, result, terceroNames, ct);
        return result;
    }

    private static Dictionary<string, Dictionary<string, JsonElement>>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try { return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(json); }
        catch (JsonException) { return null; } // una ficha corrupta no tumba el reporte
    }

    private static string? Read(Dictionary<string, Dictionary<string, JsonElement>> doc, string seccion, string campo)
        => doc.TryGetValue(seccion, out var campos) && campos.TryGetValue(campo, out var v) ? Scalar(v) : null;

    /// <summary>Valor tolerante: escalar directo, objeto envuelto <c>{ "type":..., "value":... }</c> o lista
    /// (se une con ", ").</summary>
    public static string? Scalar(JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.String:
                var s = v.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            case JsonValueKind.Number:
                return v.GetRawText();
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.Object:
                foreach (var p in v.EnumerateObject())
                {
                    if (string.Equals(p.Name, "value", StringComparison.OrdinalIgnoreCase)) { return Scalar(p.Value); }
                }
                return null;
            case JsonValueKind.Array:
                var parts = v.EnumerateArray().Select(Scalar).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                return parts.Count == 0 ? null : string.Join(", ", parts);
            default:
                return null;
        }
    }

    /// <summary>Lista de contenedor (Lookup): Id de fila -> etiqueta. Lista del directorio: Id de tercero -> nombre.
    /// Por lote; si no se resuelve (fila borrada) queda el valor crudo.</summary>
    private static async Task ResolveLabelsAsync(IApplicationDbContext db, IReadOnlyList<FichaField> fields,
        Dictionary<Guid, string?[]> values, IReadOnlyDictionary<Guid, string> terceroNames, CancellationToken ct)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            if (f.FieldType is not (TerceroFieldType.Lookup or TerceroFieldType.DirectoryLookup)) { continue; }

            var ids = values.Values.Select(v => Guid.TryParse(v[i], out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty).Distinct().ToList();
            if (ids.Count == 0) { continue; }

            Dictionary<Guid, string> labels;
            if (f.FieldType == TerceroFieldType.DirectoryLookup)
            {
                labels = ids.Where(terceroNames.ContainsKey).ToDictionary(g => g, g => terceroNames[g]);
                var faltan = ids.Where(g => !labels.ContainsKey(g)).ToList();
                if (faltan.Count > 0)
                {
                    foreach (var t in await db.Terceros.AsNoTracking().Where(t => faltan.Contains(t.Id)).Select(t => new { t.Id, t.Nombre }).ToListAsync(ct))
                    {
                        labels[t.Id] = t.Nombre;
                    }
                }
            }
            else
            {
                if (DataLookupConfig.TryParse(f.Options) is not { } cfg) { continue; }
                var displayId = await DisplayColumnAsync(db, cfg.TableId, cfg.DisplayColumnId, ct);
                if (displayId is not Guid col) { continue; }
                labels = (await db.DataContainerCells.AsNoTracking()
                        .Where(c => ids.Contains(c.RowId) && c.ColumnId == col && c.Row!.ContainerId == cfg.TableId)
                        .Select(c => new { c.RowId, c.Value })
                        .ToListAsync(ct))
                    .Where(c => !string.IsNullOrWhiteSpace(c.Value))
                    .GroupBy(c => c.RowId)
                    .ToDictionary(g => g.Key, g => g.First().Value!);
            }

            foreach (var v in values.Values)
            {
                if (Guid.TryParse(v[i], out var g) && labels.TryGetValue(g, out var lbl)) { v[i] = lbl; }
            }
        }
    }

    // Misma regla que DataLookupService: la columna elegida si sigue viva; si no, la primera de texto.
    private static async Task<Guid?> DisplayColumnAsync(IApplicationDbContext db, Guid tableId, Guid? preferida, CancellationToken ct)
    {
        var cols = await db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == tableId)
            .OrderBy(c => c.SortOrder)
            .Select(c => new { c.Id, c.Type })
            .ToListAsync(ct);
        if (preferida is Guid p && cols.Any(c => c.Id == p)) { return p; }
        return cols.FirstOrDefault(c => c.Type == DataContainerColumnType.Text)?.Id
            ?? cols.FirstOrDefault(c => c.Type != DataContainerColumnType.Submodel)?.Id;
    }

    private static string Slug(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            sb.Append(ch is >= 'a' and <= 'z' or >= '0' and <= '9' ? ch : '_');
        }
        return sb.ToString();
    }
}
