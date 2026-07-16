using System.Globalization;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.MenuConfig;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.DataContainers;

/// <inheritdoc cref="IDataContainerModuleService"/>
public sealed class DataContainerModuleService : IDataContainerModuleService
{
    private readonly IApplicationDbContext _db;
    private readonly IMenuConfigService _menu;

    public DataContainerModuleService(IApplicationDbContext db, IMenuConfigService menu)
    {
        _db = db;
        _menu = menu;
    }

    public async Task<DataContainerModuleDto?> GetAsync(Guid containerId, CancellationToken ct = default)
    {
        var c = await _db.DataContainers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == containerId, ct);
        return c is null ? null : ToDto(c);
    }

    public async Task<DataContainerModuleDto?> ResolveByRouteAsync(string route, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(route)) { return null; }
        var normalized = route.Trim().TrimStart('/');
        var c = await _db.DataContainers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ModuleRoute == normalized, ct);
        // Con ruta pero sin nodo = despublicada: la pagina no debe responder.
        return c is null || c.MenuNodeId is null ? null : ToDto(c);
    }

    public async Task<ModulePublishResult<DataContainerModuleDto>> PublishAsync(
        PublishContainerRequest request, CancellationToken ct = default)
    {
        var container = await _db.DataContainers
            .FirstOrDefaultAsync(x => x.Id == request.ContainerId, ct);
        if (container is null)
        {
            return ModulePublishResult<DataContainerModuleDto>.NotFound("La tabla no existe.");
        }
        if (container.ParentContainerId is not null)
        {
            return ModulePublishResult<DataContainerModuleDto>.Invalid(
                "Solo se publican tablas raiz: un submodelo se edita dentro de la fila de su tabla padre.");
        }

        // Las columnas elegidas deben ser de ESTA tabla (una config cruzada dejaria la grilla vacia).
        var ownColumnIds = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == container.Id)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (Unknown(request.ListColumnIds, ownColumnIds) || Unknown(request.FilterColumnIds, ownColumnIds))
        {
            return ModulePublishResult<DataContainerModuleDto>.Invalid(
                "Hay columnas que no pertenecen a esta tabla.");
        }

        // La ruta se congela la PRIMERA vez y ya no cambia: es la clave del modulo en la matriz de
        // roles, asi que re-generarla al renombrar dejaria huerfanos los permisos ya asignados.
        if (string.IsNullOrWhiteSpace(container.ModuleRoute))
        {
            container.ModuleRoute = await NextFreeRouteAsync(container.Name, ct);
        }

        container.ModuleIcon = Blank(request.Icon);
        container.ListColumnsJson = ToJson(request.ListColumnIds);
        container.FilterColumnsJson = ToJson(request.FilterColumnIds);

        if (container.MenuNodeId is null)
        {
            var node = await _menu.CreateNodeAsync(
                request.MenuViewId, request.ParentNodeId, MenuNodeKind.Item,
                container.Name, iconKey: container.ModuleIcon, legacyCode: null,
                route: container.ModuleRoute, cancellationToken: ct);
            if (!node.IsOk || node.Value is null)
            {
                return ModulePublishResult<DataContainerModuleDto>.Invalid(
                    node.Error ?? "No se pudo crear el item de menu.");
            }
            container.MenuNodeId = node.Value.Id;
        }
        else
        {
            // Ya publicada: se reconcilia el nodo (nombre e icono), nunca la ruta.
            await _menu.UpdateNodeAsync(container.MenuNodeId.Value,
                new MenuNodeEditDto(Name: container.Name, IconKey: container.ModuleIcon), ct);
        }

        await _db.SaveChangesAsync(ct);
        return ModulePublishResult<DataContainerModuleDto>.Ok(ToDto(container));
    }

    public async Task<ModulePublishResult<bool>> UnpublishAsync(Guid containerId, CancellationToken ct = default)
    {
        var container = await _db.DataContainers.FirstOrDefaultAsync(x => x.Id == containerId, ct);
        if (container is null) { return ModulePublishResult<bool>.NotFound("La tabla no existe."); }
        if (container.MenuNodeId is null) { return ModulePublishResult<bool>.Ok(true); }

        await _menu.DeleteNodeAsync(container.MenuNodeId.Value, ct);
        container.MenuNodeId = null;
        // OJO: ModuleRoute se CONSERVA a proposito. Si se republica, se reusa la misma ruta y los
        // permisos que los roles ya tenian sobre ese modulo siguen valiendo.
        await _db.SaveChangesAsync(ct);
        return ModulePublishResult<bool>.Ok(true);
    }

    public async Task<bool> SyncNodeNameAsync(Guid containerId, CancellationToken ct = default)
    {
        var container = await _db.DataContainers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == containerId, ct);
        if (container?.MenuNodeId is null) { return false; }
        var res = await _menu.UpdateNodeAsync(container.MenuNodeId.Value,
            new MenuNodeEditDto(Name: container.Name), ct);
        return res.IsOk;
    }

    // ---- Helpers ----

    private static bool Unknown(IReadOnlyList<Guid>? picked, IReadOnlyCollection<Guid> own)
        => picked is { Count: > 0 } && picked.Any(id => !own.Contains(id));

    private static string? ToJson(IReadOnlyList<Guid>? ids)
        => ids is { Count: > 0 } ? JsonSerializer.Serialize(ids) : null;

    private static IReadOnlyList<Guid> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<Guid>(); }
        try { return JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>(); }
        catch (JsonException) { return Array.Empty<Guid>(); }
    }

    /// <summary>Primera ruta libre para el nombre dado (unica por tenant; sufija -2, -3... si choca).</summary>
    private async Task<string> NextFreeRouteAsync(string name, CancellationToken ct)
    {
        var baseSlug = Slugify(name);
        var taken = await _db.DataContainers.AsNoTracking()
            .Where(c => c.ModuleRoute != null)
            .Select(c => c.ModuleRoute!)
            .ToListAsync(ct);
        var used = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidate = IDataContainerModuleService.RoutePrefix + baseSlug;
        var n = 1;
        while (used.Contains(candidate))
        {
            n++;
            candidate = $"{IDataContainerModuleService.RoutePrefix}{baseSlug}-{n}";
        }
        return candidate;
    }

    /// <summary>Nombre -> slug ASCII apto para URL ("Ordenes de Compra" -> "ordenes-de-compra").</summary>
    private static string Slugify(string name)
    {
        // Descompone y descarta los diacriticos: "Ordenes" y "Órdenes" caen en el mismo slug.
        var normalized = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) { continue; }
            if (char.IsLetterOrDigit(ch) && ch < 128) { sb.Append(ch); }
            else if (ch is ' ' or '-' or '_' or '.') { sb.Append('-'); }
        }
        var slug = sb.ToString();
        while (slug.Contains("--")) { slug = slug.Replace("--", "-"); }
        slug = slug.Trim('-');
        // Un nombre sin nada aprovechable (ej. solo simbolos) igual necesita ruta estable.
        return slug.Length == 0 ? "tabla" : slug[..Math.Min(slug.Length, 60)];
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static DataContainerModuleDto ToDto(Domain.Entities.DataContainer c) => new(
        ContainerId: c.Id,
        ContainerName: c.Name,
        ModuleRoute: c.ModuleRoute,
        MenuNodeId: c.MenuNodeId,
        IsPublished: c.MenuNodeId is not null,
        ModuleIcon: c.ModuleIcon,
        ListColumnIds: FromJson(c.ListColumnsJson),
        FilterColumnIds: FromJson(c.FilterColumnsJson));
}
