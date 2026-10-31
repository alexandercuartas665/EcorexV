using System.Globalization;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Requerimientos;

/// <summary>
/// Datos de RETORNO (Requerimientos -> formulario) listos para que el renderer los escriba en el grid
/// de origen: por cada fila (emparejada por la descripcion/producto) los valores pedidos (valor cotizado,
/// estado, proveedor...). La configuracion vive en el override de la regla GENERAR_REQUERIMIENTOS_DESDE_TABLA
/// (clave "retorno": { "<columnaDelGrid>": "<fuente>" }).
/// </summary>
public sealed record RequerimientoReturnData(
    string GridFieldCode,
    string MatchDescripcionCol,
    IReadOnlyDictionary<string, string> ColumnSources,
    IReadOnlyDictionary<string, Dictionary<string, string>> ValuesByKey);

public interface IRequerimientoReturnService
{
    /// <summary>Resuelve los datos de retorno para un formulario embebido en una tarea (Reference = numero de
    /// tarea). Null si el formulario no tiene regla de requerimientos con retorno configurado, o no hay tarea.</summary>
    Task<RequerimientoReturnData?> GetGridReturnsAsync(Guid definitionId, string taskNumber, CancellationToken ct = default);
}

public sealed class RequerimientoReturnService : IRequerimientoReturnService
{
    private readonly IApplicationDbContext _db;
    public RequerimientoReturnService(IApplicationDbContext db) { _db = db; }

    public const string VerbName = "GENERAR_REQUERIMIENTOS_DESDE_TABLA";

    public async Task<RequerimientoReturnData?> GetGridReturnsAsync(Guid definitionId, string taskNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskNumber)) { return null; }

        // 1) Override de la regla de requerimientos de ESTE formulario (el que define grilla + retorno).
        var rule = await (from fr in _db.FormFieldRules.AsNoTracking()
                          join q in _db.FormQuestions.AsNoTracking() on fr.FormQuestionId equals q.Id
                          join r in _db.Rules.AsNoTracking() on fr.RuleId equals r.Id
                          where q.DefinitionId == definitionId && r.VerbName == VerbName
                          select new { fr.ParamsOverrideJson, r.ParamsJson })
                         .FirstOrDefaultAsync(ct);
        if (rule is null) { return null; }

        // El override (por formulario) gana; si no hay, los parametros base de la regla.
        var cfg = ParseConfig(rule.ParamsOverrideJson) ?? ParseConfig(rule.ParamsJson);
        if (cfg is null || cfg.Retorno.Count == 0 || string.IsNullOrWhiteSpace(cfg.GridField)
            || string.IsNullOrWhiteSpace(cfg.DescripcionCol))
        {
            return null;
        }

        // 2) La tarea anfitriona (por numero) y sus necesidades + hijos (valores cotizados).
        var taskId = await _db.TaskItems.AsNoTracking()
            .Where(t => t.Number == taskNumber).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (taskId is null) { return null; }

        var needs = await _db.RequerimientosCompra.AsNoTracking()
            .Where(x => x.SourceTaskItemId == taskId)
            .Select(x => new
            {
                x.Descripcion,
                x.Cantidad,
                x.Estado,
                Hijos = x.Hijos.Select(h => new { h.PrecioUnitario, h.Cantidad, h.ProveedorId }).ToList()
            })
            .ToListAsync(ct);
        if (needs.Count == 0) { return null; }

        // Nombres de proveedor (si alguna fuente lo pide).
        var wantsProveedor = cfg.Retorno.Values.Any(s => s == "proveedor");
        var provNames = new Dictionary<Guid, string>();
        if (wantsProveedor)
        {
            var provIds = needs.SelectMany(n => n.Hijos).Where(h => h.ProveedorId != null)
                .Select(h => h.ProveedorId!.Value).Distinct().ToList();
            if (provIds.Count > 0)
            {
                provNames = await _db.Terceros.AsNoTracking()
                    .Where(t => provIds.Contains(t.Id))
                    .ToDictionaryAsync(t => t.Id, t => t.Nombre, ct);
            }
        }

        // 3) Agrega por clave (descripcion normalizada): suma cotizados si varias necesidades comparten producto.
        var acc = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in needs)
        {
            var key = NormDesc(n.Descripcion);
            if (string.IsNullOrEmpty(key)) { continue; }

            var cotTotal = n.Hijos.Sum(h => h.PrecioUnitario * h.Cantidad);
            var cotUnit = n.Cantidad > 0 ? cotTotal / n.Cantidad : 0m;
            var provNombre = n.Hijos.Where(h => h.ProveedorId != null)
                .Select(h => provNames.GetValueOrDefault(h.ProveedorId!.Value) ?? "").FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? "";

            if (!acc.TryGetValue(key, out var vals))
            {
                vals = new Dictionary<string, string>(StringComparer.Ordinal);
                acc[key] = vals;
            }
            // cotizado_total/unit se suman entre necesidades del mismo producto; estado/proveedor toman el ultimo no vacio.
            vals["cotizado_total"] = (Parse(vals.GetValueOrDefault("cotizado_total")) + cotTotal).ToString(CultureInfo.InvariantCulture);
            vals["cotizado_unit"] = (Parse(vals.GetValueOrDefault("cotizado_unit")) + cotUnit).ToString(CultureInfo.InvariantCulture);
            vals["estado"] = EstadoLabel(n.Estado);
            if (!string.IsNullOrEmpty(provNombre)) { vals["proveedor"] = provNombre; }
        }

        return new RequerimientoReturnData(cfg.GridField!, cfg.DescripcionCol!, cfg.Retorno, acc);
    }

    private sealed record Config(string? GridField, string? DescripcionCol, Dictionary<string, string> Retorno);

    /// <summary>Lee grilla + columna de producto + mapa de retorno del JSON de la regla.</summary>
    private static Config? ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return null; }

            string? Str(string name) => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

            var grid = Str("tableField");

            // Columna de producto: del mapeo "descripcion" (si es col:X), o productoCol, o "producto".
            string? descCol = null;
            if (root.TryGetProperty("mapeo", out var mp) && mp.ValueKind == JsonValueKind.Object
                && mp.TryGetProperty("descripcion", out var dv) && dv.ValueKind == JsonValueKind.String)
            {
                var expr = dv.GetString() ?? "";
                var i = expr.IndexOf(':');
                if (i >= 0 && expr[..i] == "col") { descCol = expr[(i + 1)..]; }
            }
            descCol ??= Str("productoCol") ?? "producto";

            var retorno = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("retorno", out var rt) && rt.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in rt.EnumerateObject())
                {
                    var src = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(src)) { retorno[p.Name] = src!; }
                }
            }
            return new Config(grid, descCol, retorno);
        }
        catch (JsonException) { return null; }
    }

    private static decimal Parse(string? s) => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0m;

    /// <summary>Clave de emparejamiento: descripcion/producto en minuscula, espacios colapsados.</summary>
    public static string NormDesc(string? s) => string.Join(' ', (s ?? "").Trim().ToLowerInvariant()
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string EstadoLabel(RequerimientoEstado e) => e switch
    {
        RequerimientoEstado.Recibido => "Recibido",
        RequerimientoEstado.Agrupado => "Agrupado",
        RequerimientoEstado.Planificado => "Planificado",
        RequerimientoEstado.ConProveedor => "Con proveedor",
        RequerimientoEstado.Ejecutado => "Ejecutado",
        RequerimientoEstado.Cerrado => "Cerrado",
        _ => e.ToString(),
    };
}
