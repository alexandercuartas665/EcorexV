using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Adaptador de lookup sobre las ACTIVIDADES (TaskItem) del tenant. Sirve, por ejemplo, para que una
/// columna "OT" de una grilla liste las actividades de un cliente y guarde su referencia (T00042).
/// Busca por titulo o referencia; el filtro global de EF acota al tenant (nunca cruza tenant).
/// Expone id / referencia / titulo / cliente / cliente_id / estado para el autollenado.
///
/// Filtro (FilterJson plano u objeto "fixed"; en la grilla admite tokens {columna} que el renderer ya
/// sustituyo con los valores de la fila, ver <see cref="FormGridFilterTokens"/>):
/// - <c>cliente</c>: id del tercero (Guid) o nombre (contiene, sin mayusculas).
/// - <c>cliente_id</c>: id del tercero.
/// - <c>tablero</c>: id del tablero.
/// - <c>archivadas</c>: "true" para incluir las archivadas (por defecto se excluyen).
/// Un filtro de cliente VACIO (la fila aun no tiene cliente) devuelve la lista vacia: es una cascada,
/// primero se elige el padre. Solo lectura: las actividades se crean en su modulo, no desde el formulario.
/// </summary>
public sealed class ActivityLookupSource : IFormLookupSource
{
    private readonly IApplicationDbContext _db;

    public ActivityLookupSource(IApplicationDbContext db) { _db = db; }

    public FormSourceKind Kind => FormSourceKind.Activity;

    private sealed record Row(Guid Id, string Number, string Title, Guid? TerceroId, string? Cliente, TaskItemStatus Status);

    public async Task<FormLookupPage> SearchAsync(FormLookupRequest request, CancellationToken cancellationToken = default)
    {
        var filter = ReadFilter(request.FilterJson, request.EqFilters);
        var query = _db.TaskItems.AsNoTracking();

        if (!IsTrue(filter.GetValueOrDefault("archivadas"))) { query = query.Where(t => !t.IsArchived); }

        foreach (var key in new[] { "cliente", "cliente_id" })
        {
            if (!filter.TryGetValue(key, out var raw)) { continue; }
            var v = raw?.Trim() ?? "";
            if (v.Length == 0) { return new FormLookupPage(Array.Empty<FormLookupItem>(), 0, false); }
            if (Guid.TryParse(v, out var terceroId))
            {
                query = query.Where(t => t.TerceroId == terceroId);
            }
            else if (key == "cliente")
            {
                var name = v.ToLower();
                query = query.Where(t => t.Tercero != null && t.Tercero.Nombre.ToLower().Contains(name));
            }
            else
            {
                return new FormLookupPage(Array.Empty<FormLookupItem>(), 0, false);
            }
        }

        if (filter.TryGetValue("tablero", out var tablero) && Guid.TryParse(tablero?.Trim(), out var boardId))
        {
            query = query.Where(t => t.BoardId == boardId);
        }

        var q = request.Query?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(q))
        {
            query = query.Where(t => t.Title.ToLower().Contains(q) || t.Number.ToLower().Contains(q));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 100))
            .Select(t => new Row(t.Id, t.Number, t.Title, t.TerceroId, t.Tercero != null ? t.Tercero.Nombre : null, t.Status))
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => ToItem(r, request.DisplayField, request.Fields)).ToList();
        return new FormLookupPage(items, total, request.Skip + rows.Count < total);
    }

    public async Task<FormLookupItem?> ResolveAsync(string sourceRef, string value, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) { return null; }
        var query = _db.TaskItems.AsNoTracking();
        // El valor guardado puede ser el id (campo) o la referencia (columna con valueField=referencia).
        query = Guid.TryParse(v, out var id) ? query.Where(t => t.Id == id) : query.Where(t => t.Number == v);
        var row = await query
            .Select(t => new Row(t.Id, t.Number, t.Title, t.TerceroId, t.Tercero != null ? t.Tercero.Nombre : null, t.Status))
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToItem(row, displayField: null, fields);
    }

    public Task<IReadOnlyList<FormLookupSourceOption>> ListSourcesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FormLookupSourceOption> one = new[] { new FormLookupSourceOption("", "Actividades") };
        return Task.FromResult(one);
    }

    public Task<IReadOnlyList<FormLookupFieldMeta>> DescribeFieldsAsync(string? sourceRef, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FormLookupFieldMeta> list = new List<FormLookupFieldMeta>
        {
            new("id", "Id"),
            new("referencia", "Referencia"),
            new("titulo", "Titulo"),
            new("cliente", "Cliente"),
            new("cliente_id", "Id del cliente"),
            new("estado", "Estado"),
        };
        return Task.FromResult(list);
    }

    private static FormLookupItem ToItem(Row r, string? displayField, IReadOnlyList<string> fields)
    {
        var all = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = r.Id.ToString(),
            ["referencia"] = r.Number,
            ["titulo"] = r.Title,
            ["cliente"] = r.Cliente,
            ["cliente_id"] = r.TerceroId?.ToString(),
            ["estado"] = r.Status.ToString(),
        };
        var display = !string.IsNullOrEmpty(displayField) && all.TryGetValue(displayField, out var d) && !string.IsNullOrEmpty(d)
            ? d!
            : $"{r.Number} - {r.Title}";
        var picked = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in fields) { picked[key] = all.TryGetValue(key, out var v) ? v : null; }
        return new FormLookupItem(r.Id.ToString(), display, picked);
    }

    /// <summary>Lee el filtro: forma plana {"k":"v"} o {"fixed":{..}}; luego los EqFilters ya resueltos
    /// (cascada de campo) pisan. Defensivo: JSON invalido = sin filtro.</summary>
    internal static Dictionary<string, string?> ReadFilter(string? filterJson, IReadOnlyDictionary<string, string>? eq)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(filterJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(filterJson);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    var src = root.TryGetProperty("fixed", out var fx) && fx.ValueKind == JsonValueKind.Object ? fx : root;
                    foreach (var p in src.EnumerateObject())
                    {
                        if (p.Value.ValueKind is JsonValueKind.String) { map[p.Name] = p.Value.GetString(); }
                        else if (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) { map[p.Name] = p.Value.GetRawText(); }
                    }
                }
            }
            catch (JsonException) { /* filtro invalido: se ignora */ }
        }
        if (eq is not null)
        {
            foreach (var (k, v) in eq) { map[k] = v; }
        }
        return map;
    }

    private static bool IsTrue(string? v) => string.Equals(v?.Trim(), "true", StringComparison.OrdinalIgnoreCase) || v?.Trim() == "1";
}
