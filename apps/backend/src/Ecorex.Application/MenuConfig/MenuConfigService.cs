using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.MenuConfig;

/// <summary>
/// Implementacion de IMenuConfigService (Ola 1 del menu por perfil). Aislamiento por tenant via
/// filtro global. La resolucion del arbol usa MenuTreeBuilder (logica pura). La clonacion recrea
/// los nodos con nuevos ids conservando la jerarquia, en transaccion.
/// </summary>
public sealed class MenuConfigService : IMenuConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public MenuConfigService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<ResolvedMenuDto?> GetMenuForTenantUserAsync(
        Guid tenantId, Guid? menuViewId, CancellationToken cancellationToken = default)
    {
        // Vista objetivo: la asignada si existe y tiene nodos visibles; si no, la IsDefault.
        MenuView? view = null;
        if (menuViewId is Guid viewId)
        {
            view = await _db.MenuViews.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == viewId, cancellationToken);
        }

        var resolved = view is not null
            ? await BuildResolvedAsync(view, cancellationToken)
            : null;

        if (resolved is null)
        {
            // Fallback a la vista por defecto del tenant.
            var defaultView = await _db.MenuViews.AsNoTracking()
                .Where(v => v.IsDefault)
                .OrderBy(v => v.SortOrder).ThenBy(v => v.Name)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await _db.MenuViews.AsNoTracking()
                    .OrderBy(v => v.SortOrder).ThenBy(v => v.Name)
                    .FirstOrDefaultAsync(cancellationToken);
            if (defaultView is not null)
            {
                resolved = await BuildResolvedAsync(defaultView, cancellationToken);
            }
        }

        return resolved;
    }

    private async Task<ResolvedMenuDto?> BuildResolvedAsync(MenuView view, CancellationToken cancellationToken)
    {
        var flat = await _db.MenuNodes.AsNoTracking()
            .Where(n => n.MenuViewId == view.Id)
            .Select(n => new MenuTreeBuilder.FlatNode(
                n.Id, n.ParentId, n.Kind, n.Name, n.IconKey, n.LegacyCode,
                n.Route, n.State, n.IsVisible, n.SortOrder))
            .ToListAsync(cancellationToken);

        var roots = MenuTreeBuilder.Build(flat);
        if (roots.Count == 0)
        {
            return null;
        }
        return new ResolvedMenuDto(view.Id, view.Name, roots);
    }

    public async Task<IReadOnlyList<MenuViewDto>> ListViewsAsync(CancellationToken cancellationToken = default)
    {
        var views = await _db.MenuViews.AsNoTracking()
            .OrderBy(v => v.SortOrder).ThenBy(v => v.Name)
            .Select(v => new MenuViewDto(
                v.Id, v.Name, v.Description, v.IsDefault, v.SortOrder,
                _db.MenuNodes.Count(n => n.MenuViewId == v.Id)))
            .ToListAsync(cancellationToken);
        return views;
    }

    public async Task<MenuConfigResult<MenuViewDto>> CreateViewAsync(
        string name, string? description = null, bool isDefault = false, int sortOrder = 0,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return MenuConfigResult<MenuViewDto>.Invalid("No hay tenant activo.");
        }
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return MenuConfigResult<MenuViewDto>.Invalid("El nombre de la vista es obligatorio.");
        }
        if (trimmed.Length > 150)
        {
            return MenuConfigResult<MenuViewDto>.Invalid("El nombre no puede superar 150 caracteres.");
        }
        if (await _db.MenuViews.AnyAsync(v => v.Name == trimmed, cancellationToken))
        {
            return MenuConfigResult<MenuViewDto>.Conflict($"Ya existe una vista con el nombre '{trimmed}'.");
        }

        var view = new MenuView
        {
            TenantId = tenantId,
            Name = trimmed,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            IsDefault = isDefault,
            SortOrder = sortOrder
        };
        _db.MenuViews.Add(view);
        await _db.SaveChangesAsync(cancellationToken);
        return MenuConfigResult<MenuViewDto>.Ok(new MenuViewDto(view.Id, view.Name, view.Description, view.IsDefault, view.SortOrder, 0));
    }

    public async Task<MenuConfigResult<MenuViewDto>> CloneViewAsync(
        Guid sourceViewId, string newName, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return MenuConfigResult<MenuViewDto>.Invalid("No hay tenant activo.");
        }
        var trimmed = newName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return MenuConfigResult<MenuViewDto>.Invalid("El nombre de la vista es obligatorio.");
        }
        if (trimmed.Length > 150)
        {
            return MenuConfigResult<MenuViewDto>.Invalid("El nombre no puede superar 150 caracteres.");
        }

        var source = await _db.MenuViews.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == sourceViewId, cancellationToken);
        if (source is null)
        {
            return MenuConfigResult<MenuViewDto>.NotFound("La vista de origen no existe.");
        }
        if (await _db.MenuViews.AnyAsync(v => v.Name == trimmed, cancellationToken))
        {
            return MenuConfigResult<MenuViewDto>.Conflict($"Ya existe una vista con el nombre '{trimmed}'.");
        }

        var sourceNodes = await _db.MenuNodes.AsNoTracking()
            .Where(n => n.MenuViewId == sourceViewId)
            .ToListAsync(cancellationToken);

        var clone = new MenuView
        {
            TenantId = tenantId,
            Name = trimmed,
            Description = source.Description,
            IsDefault = false, // la copia nunca es la vista por defecto.
            SortOrder = source.SortOrder
        };

        // Mapa old->new id para reconstruir ParentId en la copia.
        var idMap = sourceNodes.ToDictionary(n => n.Id, _ => Guid.CreateVersion7());
        var cloneNodes = sourceNodes.Select(n => new MenuNode
        {
            Id = idMap[n.Id],
            TenantId = tenantId,
            MenuViewId = clone.Id,
            ParentId = n.ParentId is Guid pid && idMap.TryGetValue(pid, out var newPid) ? newPid : null,
            Kind = n.Kind,
            Name = n.Name,
            IconKey = n.IconKey,
            LegacyCode = n.LegacyCode,
            Route = n.Route,
            Description = n.Description,
            HelpText = n.HelpText,
            State = n.State,
            IsVisible = n.IsVisible,
            SortOrder = n.SortOrder
        }).ToList();

        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            _db.MenuViews.Add(clone);
            _db.MenuNodes.AddRange(cloneNodes);
            await _db.SaveChangesAsync(cancellationToken);
            if (tx is not null) { await tx.CommitAsync(cancellationToken); }
        }
        catch
        {
            if (tx is not null) { await tx.RollbackAsync(cancellationToken); }
            throw;
        }
        finally
        {
            if (tx is not null) { await tx.DisposeAsync(); }
        }

        return MenuConfigResult<MenuViewDto>.Ok(new MenuViewDto(
            clone.Id, clone.Name, clone.Description, clone.IsDefault, clone.SortOrder, cloneNodes.Count));
    }
}
