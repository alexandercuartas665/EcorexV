using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Adaptador de lookup sobre los ORIGENES de Requerimientos de Compra (RequerimientoOrigen) del tenant.
/// Sirve para que una columna "Origen" de una grilla (p.ej. la requisicion de materiales) deje elegir
/// por cada item de donde llega la necesidad (Insumos de Oficina, Planta, Reabastecimiento...). La lista
/// sale de la configuracion del modulo, asi que un origen nuevo aparece sin tocar el formulario.
/// Solo origenes activos; el filtro global de EF acota al tenant. Expone id / nombre / area.
/// El verbo GENERAR_REQUERIMIENTOS_DESDE_TABLA acepta el id o el nombre (mapeo "origen").
/// </summary>
public sealed class RequerimientoOrigenLookupSource : IFormLookupSource
{
    private readonly IApplicationDbContext _db;

    public RequerimientoOrigenLookupSource(IApplicationDbContext db) { _db = db; }

    public FormSourceKind Kind => FormSourceKind.RequerimientoOrigen;

    private sealed record Row(Guid Id, string Nombre, string Area);

    public async Task<FormLookupPage> SearchAsync(FormLookupRequest request, CancellationToken cancellationToken = default)
    {
        var query = _db.RequerimientoOrigenes.AsNoTracking().Where(o => o.Activo);
        var q = request.Query?.Trim().ToLower();
        if (!string.IsNullOrEmpty(q))
        {
            query = query.Where(o => o.Nombre.ToLower().Contains(q) || o.Area.ToLower().Contains(q));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(o => o.Nombre)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 100))
            .Select(o => new Row(o.Id, o.Nombre, o.Area))
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => ToItem(r, request.DisplayField, request.Fields)).ToList();
        return new FormLookupPage(items, total, request.Skip + rows.Count < total);
    }

    public async Task<FormLookupItem?> ResolveAsync(string sourceRef, string value, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) { return null; }
        var query = _db.RequerimientoOrigenes.AsNoTracking();
        // El valor guardado puede ser el id o el nombre (columna con valueField=nombre).
        if (Guid.TryParse(v, out var id))
        {
            query = query.Where(o => o.Id == id);
        }
        else
        {
            var lower = v.ToLower();
            query = query.Where(o => o.Nombre.ToLower() == lower);
        }
        var row = await query.Select(o => new Row(o.Id, o.Nombre, o.Area)).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToItem(row, displayField: null, fields);
    }

    public Task<IReadOnlyList<FormLookupSourceOption>> ListSourcesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FormLookupSourceOption> one = new[] { new FormLookupSourceOption("", "Origenes de compra") };
        return Task.FromResult(one);
    }

    public Task<IReadOnlyList<FormLookupFieldMeta>> DescribeFieldsAsync(string? sourceRef, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FormLookupFieldMeta> list = new List<FormLookupFieldMeta>
        {
            new("id", "Id"),
            new("nombre", "Nombre"),
            new("area", "Area"),
        };
        return Task.FromResult(list);
    }

    private static FormLookupItem ToItem(Row r, string? displayField, IReadOnlyList<string> fields)
    {
        var all = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = r.Id.ToString(),
            ["nombre"] = r.Nombre,
            ["area"] = r.Area,
        };
        var display = !string.IsNullOrEmpty(displayField) && all.TryGetValue(displayField, out var d) && !string.IsNullOrEmpty(d)
            ? d!
            : r.Nombre;
        var picked = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in fields) { picked[key] = all.TryGetValue(key, out var v) ? v : null; }
        return new FormLookupItem(r.Id.ToString(), display, picked);
    }
}
