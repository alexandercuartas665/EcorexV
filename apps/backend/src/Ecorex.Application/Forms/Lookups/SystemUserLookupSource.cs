using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Adaptador de lookup sobre los USUARIOS del tenant (modulo 1.2). El valor guardado es el Id del
/// TenantUser; el catalogo esta acotado al tenant por <see cref="ITenantUserService"/> (nunca cruza
/// tenant, nunca trae usuarios eliminados). Se usa, por ejemplo, para un campo "Redireccionar a" que
/// debe listar los usuarios del sistema y venir por defecto con el usuario asignado a la tarea
/// (<see cref="FormDefaultDynamic.AssignedUser"/>). Expone id / nombre / email para el autollenado.
/// Solo lectura: los usuarios se crean en su propio modulo, no desde el formulario.
/// </summary>
public sealed class SystemUserLookupSource : IFormLookupSource
{
    private readonly ITenantUserService _users;

    public SystemUserLookupSource(ITenantUserService users) { _users = users; }

    public FormSourceKind Kind => FormSourceKind.SystemUser;

    public async Task<FormLookupPage> SearchAsync(FormLookupRequest request, CancellationToken cancellationToken = default)
    {
        // Los usuarios de un tenant son pocos (decenas): se lista y filtra en memoria sin costo real.
        var all = await _users.ListAsync(cancellationToken);

        // Lista blanca OPCIONAL (FilterJson {"users":[..]}): si el disenador eligio un subconjunto, el
        // catalogo se acota a esos ids; vacio/ausente = todos (comportamiento original). Ver FormSystemUserFilter.
        var whitelist = FormSystemUserFilter.Parse(request.FilterJson);
        IEnumerable<TenantUserDto> catalog = all;
        if (whitelist.HasWhitelist)
        {
            var allow = whitelist.UserIds
                .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
                .Where(g => g is not null)
                .Select(g => g!.Value)
                .ToHashSet();
            catalog = all.Where(u => allow.Contains(u.Id));
        }

        var q = request.Query?.Trim().ToLowerInvariant();
        IEnumerable<TenantUserDto> filtered = catalog;
        if (!string.IsNullOrEmpty(q))
        {
            filtered = catalog.Where(u =>
                Label(u).ToLowerInvariant().Contains(q)
                || u.Email.ToLowerInvariant().Contains(q));
        }
        var ordered = filtered.OrderBy(Label, StringComparer.OrdinalIgnoreCase).ToList();
        var total = ordered.Count;
        var page = ordered
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 100))
            .Select(u => ToItem(u, request.DisplayField, request.Fields))
            .ToList();
        return new FormLookupPage(page, total, request.Skip + page.Count < total);
    }

    public async Task<FormLookupItem?> ResolveAsync(string sourceRef, string value, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(value, out var id)) { return null; }
        var all = await _users.ListAsync(cancellationToken);
        var u = all.FirstOrDefault(x => x.Id == id);
        return u is null ? null : ToItem(u, displayField: null, fields);
    }

    public Task<IReadOnlyList<FormLookupSourceOption>> ListSourcesAsync(CancellationToken cancellationToken = default)
    {
        // Fuente unica (no hay varias tablas que elegir); SourceRef va vacio.
        IReadOnlyList<FormLookupSourceOption> one = new[] { new FormLookupSourceOption("", "Usuarios del sistema") };
        return Task.FromResult(one);
    }

    public Task<IReadOnlyList<FormLookupFieldMeta>> DescribeFieldsAsync(string? sourceRef, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FormLookupFieldMeta> list = new List<FormLookupFieldMeta>
        {
            new("id", "Id"),
            new("nombre", "Nombre"),
            new("email", "Email"),
        };
        return Task.FromResult(list);
    }

    private static FormLookupItem ToItem(TenantUserDto u, string? displayField, IReadOnlyList<string> fields)
    {
        var all = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = u.Id.ToString(),
            ["nombre"] = Label(u),
            ["email"] = u.Email,
        };
        var display = !string.IsNullOrEmpty(displayField) && all.TryGetValue(displayField, out var d) && !string.IsNullOrEmpty(d)
            ? d! : Label(u);
        var picked = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in fields) { picked[key] = all.TryGetValue(key, out var v) ? v : null; }
        return new FormLookupItem(u.Id.ToString(), display, picked);
    }

    /// <summary>Nombre legible: DisplayName si existe; si no, la parte local del email con cada palabra
    /// capitalizada ("ana.garcia@x.com" -> "Ana Garcia"). Misma regla que TaskUi.UserLabel, replicada aqui
    /// porque aquel vive en la capa de UI y esta fuente es de Application.</summary>
    private static string Label(TenantUserDto u)
    {
        if (!string.IsNullOrWhiteSpace(u.DisplayName)) { return u.DisplayName!.Trim(); }
        var local = u.Email;
        var at = local.IndexOf('@');
        if (at > 0) { local = local[..at]; }
        var parts = local.Split(['.', '-', '_', '+'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { return u.Email; }
        return string.Join(" ", parts.Select(p =>
            p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }
}
