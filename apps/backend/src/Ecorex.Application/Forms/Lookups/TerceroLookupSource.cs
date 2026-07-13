using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Directorio;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Adaptador de lookup sobre el Directorio de terceros (ola F1, doc 01 D4). El valor guardado es
/// el id del tercero; el catalogo esta acotado al tenant por el filtro global de EF (imposible
/// autocompletar con datos de otro cliente). Expone campos estandar y los campos dinamicos de
/// las fichas (<see cref="Tercero.FichasJson"/>) para el autollenado.
/// </summary>
public sealed class TerceroLookupSource : IFormLookupSource
{
    private readonly IApplicationDbContext _db;
    private readonly ITerceroFieldService _fields;

    public TerceroLookupSource(IApplicationDbContext db, ITerceroFieldService fields)
    {
        _db = db;
        _fields = fields;
    }

    public FormSourceKind Kind => FormSourceKind.Tercero;

    public async Task<FormLookupPage> SearchAsync(FormLookupRequest request, CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(_db.Terceros.AsNoTracking(), request.FilterJson);

        var q = request.Query?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(q))
        {
            query = query.Where(t =>
                t.Nombre.ToLower().Contains(q)
                || (t.IdValor != null && t.IdValor.ToLower().Contains(q))
                || (t.Email != null && t.Email.ToLower().Contains(q))
                || (t.Ciudad != null && t.Ciudad.ToLower().Contains(q)));
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(t => t.Nombre)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 100))
            .Select(t => new Row(t.Id, t.Nombre, t.IdValor, t.Ciudad, t.Email, t.Telefono, t.Vendedor, t.Sector, t.Cargo, t.Estado, t.FichasJson))
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => ToItem(r, request.DisplayField, request.Fields)).ToList();
        return new FormLookupPage(items, total, request.Skip + rows.Count < total);
    }

    public async Task<FormLookupItem?> ResolveAsync(string sourceRef, string value, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(value, out var id)) { return null; }

        var r = await _db.Terceros.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new Row(t.Id, t.Nombre, t.IdValor, t.Ciudad, t.Email, t.Telefono, t.Vendedor, t.Sector, t.Cargo, t.Estado, t.FichasJson))
            .FirstOrDefaultAsync(cancellationToken);

        return r is null ? null : ToItem(r, displayField: null, fields);
    }

    public Task<IReadOnlyList<FormLookupSourceOption>> ListSourcesAsync(CancellationToken cancellationToken = default)
    {
        // El Directorio es una fuente unica (no requiere elegir una tabla); SourceRef va vacio.
        IReadOnlyList<FormLookupSourceOption> one = new[] { new FormLookupSourceOption("", "Directorio (terceros)") };
        return Task.FromResult(one);
    }

    public async Task<IReadOnlyList<FormLookupFieldMeta>> DescribeFieldsAsync(string? sourceRef, CancellationToken cancellationToken = default)
    {
        var list = new List<FormLookupFieldMeta>
        {
            new("id", "Id"),
            new("nombre", "Nombre"),
            new("identificacion", "Identificacion (NIT/CC)"),
            new("ciudad", "Ciudad"),
            new("email", "Email"),
            new("telefono", "Telefono"),
            new("vendedor", "Vendedor"),
            new("sector", "Sector"),
            new("cargo", "Cargo"),
            new("estado", "Estado"),
        };

        // Campos dinamicos (fichas) del tenant, para el mapa de autollenado.
        var dyn = await _fields.ListFieldsAsync(cancellationToken);
        foreach (var f in dyn)
        {
            if (!list.Any(x => string.Equals(x.Key, f.FieldKey, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(new FormLookupFieldMeta(f.FieldKey, string.IsNullOrWhiteSpace(f.Label) ? f.FieldKey : f.Label, IsDynamic: true));
            }
        }
        return list;
    }

    private static FormLookupItem ToItem(Row r, string? displayField, IReadOnlyList<string> fields)
    {
        var all = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = r.Id.ToString(),
            ["nombre"] = r.Nombre,
            ["identificacion"] = r.IdValor,
            ["ciudad"] = r.Ciudad,
            ["email"] = r.Email,
            ["telefono"] = r.Telefono,
            ["vendedor"] = r.Vendedor,
            ["sector"] = r.Sector,
            ["cargo"] = r.Cargo,
            ["estado"] = r.Estado.ToString(),
        };
        FlattenFichas(r.FichasJson, all);

        var display = !string.IsNullOrEmpty(displayField) && all.TryGetValue(displayField, out var d) && !string.IsNullOrEmpty(d)
            ? d! : r.Nombre;

        var picked = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in fields)
        {
            picked[key] = all.TryGetValue(key, out var v) ? v : null;
        }
        return new FormLookupItem(r.Id.ToString(), display, picked);
    }

    /// <summary>Filtro de catalogo (FilterJson) sobre campos escalares conocidos. Defensivo:
    /// claves desconocidas o JSON invalido se ignoran (no rompe la busqueda).</summary>
    private static IQueryable<Tercero> ApplyFilter(IQueryable<Tercero> query, string? filterJson)
    {
        if (string.IsNullOrWhiteSpace(filterJson)) { return query; }
        try
        {
            using var doc = JsonDocument.Parse(filterJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return query; }
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var val = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
                if (string.IsNullOrWhiteSpace(val)) { continue; }
                switch (p.Name.ToLowerInvariant())
                {
                    case "ciudad": query = query.Where(t => t.Ciudad == val); break;
                    case "vendedor": query = query.Where(t => t.Vendedor == val); break;
                    case "sector": query = query.Where(t => t.Sector == val); break;
                    case "perfil":
                        if (Enum.TryParse<TerceroPerfil>(val, ignoreCase: true, out var perfil))
                        {
                            query = query.Where(t => (t.Perfiles & perfil) == perfil);
                        }
                        break;
                    case "estado":
                        if (Enum.TryParse<TerceroEstado>(val, ignoreCase: true, out var estado))
                        {
                            query = query.Where(t => t.Estado == estado);
                        }
                        break;
                }
            }
        }
        catch (JsonException) { /* filtro invalido: se ignora */ }
        return query;
    }

    /// <summary>Aplana FichasJson ({ ficha: { campo: valor } }) a campo->valor (ultimo gana).</summary>
    private static void FlattenFichas(string? fichasJson, Dictionary<string, string?> into)
    {
        if (string.IsNullOrWhiteSpace(fichasJson)) { return; }
        try
        {
            using var doc = JsonDocument.Parse(fichasJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return; }
            foreach (var ficha in doc.RootElement.EnumerateObject())
            {
                if (ficha.Value.ValueKind != JsonValueKind.Object) { continue; }
                foreach (var campo in ficha.Value.EnumerateObject())
                {
                    into[campo.Name] = campo.Value.ValueKind switch
                    {
                        JsonValueKind.String => campo.Value.GetString(),
                        JsonValueKind.Null => null,
                        _ => campo.Value.ToString(),
                    };
                }
            }
        }
        catch (JsonException) { /* json invalido: se ignora */ }
    }

    private sealed record Row(
        Guid Id, string Nombre, string? IdValor, string? Ciudad, string? Email, string? Telefono,
        string? Vendedor, string? Sector, string? Cargo, TerceroEstado Estado, string? FichasJson);
}
