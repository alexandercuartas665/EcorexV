using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
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
            // Fallback a la vista por defecto del tenant. Si NINGUNA esta marcada IsDefault (caso de
            // tenants reales creados sin seed de menu, ej. BITCODE), se cae a la vista mas RICA
            // (mayor numero de nodos visibles) para que el usuario sin vista asignada vea el menu
            // completo disponible en vez de una vista minima/E2E. (#2)
            var defaultView = await _db.MenuViews.AsNoTracking()
                .Where(v => v.IsDefault)
                .OrderBy(v => v.SortOrder).ThenBy(v => v.Name)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await _db.MenuViews.AsNoTracking()
                    .OrderByDescending(v => _db.MenuNodes.Count(n => n.MenuViewId == v.Id && n.IsVisible))
                    .ThenBy(v => v.SortOrder).ThenBy(v => v.Name)
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
                n.Route, n.State, n.IsVisible, n.SortOrder, n.IsProcessGroup))
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
            SortOrder = n.SortOrder,
            IsProcessGroup = n.IsProcessGroup
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

    // ================= Ola 2: edicion de vistas =================

    public async Task<MenuConfigResult<MenuViewDto>> UpdateViewAsync(
        Guid viewId, string name, string? description, CancellationToken cancellationToken = default)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return MenuConfigResult<MenuViewDto>.Invalid("El nombre de la vista es obligatorio.");
        }
        if (trimmed.Length > 150)
        {
            return MenuConfigResult<MenuViewDto>.Invalid("El nombre no puede superar 150 caracteres.");
        }

        var view = await _db.MenuViews.FirstOrDefaultAsync(v => v.Id == viewId, cancellationToken);
        if (view is null)
        {
            return MenuConfigResult<MenuViewDto>.NotFound("La vista no existe.");
        }
        if (await _db.MenuViews.AnyAsync(v => v.Id != viewId && v.Name == trimmed, cancellationToken))
        {
            return MenuConfigResult<MenuViewDto>.Conflict($"Ya existe una vista con el nombre '{trimmed}'.");
        }

        view.Name = trimmed;
        view.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        await _db.SaveChangesAsync(cancellationToken);

        var nodeCount = await _db.MenuNodes.CountAsync(n => n.MenuViewId == viewId, cancellationToken);
        return MenuConfigResult<MenuViewDto>.Ok(new MenuViewDto(
            view.Id, view.Name, view.Description, view.IsDefault, view.SortOrder, nodeCount));
    }

    public async Task<MenuConfigResult<bool>> DeleteViewAsync(Guid viewId, CancellationToken cancellationToken = default)
    {
        var view = await _db.MenuViews.FirstOrDefaultAsync(v => v.Id == viewId, cancellationToken);
        if (view is null)
        {
            return MenuConfigResult<bool>.NotFound("La vista no existe.");
        }
        if (view.IsDefault)
        {
            return MenuConfigResult<bool>.Invalid("No se puede eliminar la vista predeterminada. Marca otra como predeterminada primero.");
        }

        var nodes = await _db.MenuNodes.Where(n => n.MenuViewId == viewId).ToListAsync(cancellationToken);

        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            // Desasigna los usuarios que apuntaban a la vista (caen a la IsDefault del tenant).
            var users = await _db.TenantUsers.Where(u => u.MenuViewId == viewId).ToListAsync(cancellationToken);
            foreach (var u in users) { u.MenuViewId = null; }

            _db.MenuNodes.RemoveRange(nodes);
            _db.MenuViews.Remove(view);
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

        return MenuConfigResult<bool>.Ok(true);
    }

    public async Task<MenuConfigResult<bool>> SetDefaultViewAsync(Guid viewId, CancellationToken cancellationToken = default)
    {
        var target = await _db.MenuViews.FirstOrDefaultAsync(v => v.Id == viewId, cancellationToken);
        if (target is null)
        {
            return MenuConfigResult<bool>.NotFound("La vista no existe.");
        }

        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            var others = await _db.MenuViews.Where(v => v.IsDefault && v.Id != viewId).ToListAsync(cancellationToken);
            foreach (var v in others) { v.IsDefault = false; }
            target.IsDefault = true;
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

        return MenuConfigResult<bool>.Ok(true);
    }

    public async Task<MenuConfigResult<MenuViewTreeDto>> GetViewTreeAsync(Guid viewId, CancellationToken cancellationToken = default)
    {
        var view = await _db.MenuViews.AsNoTracking().FirstOrDefaultAsync(v => v.Id == viewId, cancellationToken);
        if (view is null)
        {
            return MenuConfigResult<MenuViewTreeDto>.NotFound("La vista no existe.");
        }

        var flat = await _db.MenuNodes.AsNoTracking()
            .Where(n => n.MenuViewId == viewId)
            .ToListAsync(cancellationToken);

        var childrenByParent = flat
            .Where(n => n.ParentId is not null)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        List<MenuEditorNodeDto> BuildLevel(Guid? parentId)
        {
            IEnumerable<MenuNode> level = parentId is null
                ? flat.Where(n => n.ParentId is null)
                : (childrenByParent.TryGetValue(parentId.Value, out var kids) ? kids : Enumerable.Empty<MenuNode>());
            return level
                .OrderBy(n => n.SortOrder).ThenBy(n => n.Name, StringComparer.Ordinal)
                .Select(n => new MenuEditorNodeDto(
                    n.Id, n.ParentId, n.Kind, n.Name, n.IconKey, n.LegacyCode, n.Route,
                    n.Description, n.HelpText, n.State, n.IsVisible, n.SortOrder, n.IsProcessGroup, BuildLevel(n.Id)))
                .ToList();
        }

        var tree = new MenuViewTreeDto(view.Id, view.Name, view.Description, view.IsDefault, BuildLevel(null));
        return MenuConfigResult<MenuViewTreeDto>.Ok(tree);
    }

    // ================= Ola 2: edicion de nodos =================

    public async Task<MenuConfigResult<MenuEditorNodeDto>> CreateNodeAsync(
        Guid viewId, Guid? parentId, MenuNodeKind kind, string name,
        string? iconKey = null, string? legacyCode = null, string? route = null,
        string? description = null, string? helpText = null, MenuNodeState state = MenuNodeState.Ready,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return MenuConfigResult<MenuEditorNodeDto>.Invalid("No hay tenant activo.");
        }
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return MenuConfigResult<MenuEditorNodeDto>.Invalid("El nombre del elemento es obligatorio.");
        }

        var view = await _db.MenuViews.AsNoTracking().FirstOrDefaultAsync(v => v.Id == viewId, cancellationToken);
        if (view is null)
        {
            return MenuConfigResult<MenuEditorNodeDto>.NotFound("La vista no existe.");
        }

        MenuNode? parent = null;
        if (parentId is Guid pid)
        {
            parent = await _db.MenuNodes.AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == pid && n.MenuViewId == viewId, cancellationToken);
            if (parent is null)
            {
                return MenuConfigResult<MenuEditorNodeDto>.NotFound("El elemento padre no existe en esta vista.");
            }
        }

        var kindError = MenuNodeKindRules.Validate(kind, parent?.Kind);
        if (kindError is not null)
        {
            return MenuConfigResult<MenuEditorNodeDto>.Invalid(kindError);
        }

        // Al final del orden de sus hermanos.
        var nextOrder = await _db.MenuNodes
            .Where(n => n.MenuViewId == viewId && n.ParentId == parentId)
            .Select(n => (int?)n.SortOrder)
            .MaxAsync(cancellationToken);

        var node = new MenuNode
        {
            TenantId = tenantId,
            MenuViewId = viewId,
            ParentId = parentId,
            Kind = kind,
            Name = trimmed,
            IconKey = string.IsNullOrWhiteSpace(iconKey) ? null : iconKey.Trim(),
            LegacyCode = string.IsNullOrWhiteSpace(legacyCode) ? null : legacyCode.Trim(),
            Route = string.IsNullOrWhiteSpace(route) ? null : route.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            HelpText = string.IsNullOrWhiteSpace(helpText) ? null : helpText.Trim(),
            State = state,
            IsVisible = true,
            SortOrder = (nextOrder ?? -1) + 1
        };
        _db.MenuNodes.Add(node);
        await _db.SaveChangesAsync(cancellationToken);

        return MenuConfigResult<MenuEditorNodeDto>.Ok(ToEditorDto(node));
    }

    public async Task<MenuConfigResult<MenuEditorNodeDto>> UpdateNodeAsync(
        Guid nodeId, MenuNodeEditDto edit, CancellationToken cancellationToken = default)
    {
        var node = await _db.MenuNodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return MenuConfigResult<MenuEditorNodeDto>.NotFound("El elemento no existe.");
        }

        if (edit.Name is not null)
        {
            var trimmed = edit.Name.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return MenuConfigResult<MenuEditorNodeDto>.Invalid("El nombre del elemento es obligatorio.");
            }
            node.Name = trimmed;
        }
        if (edit.IconKey is not null) { node.IconKey = string.IsNullOrWhiteSpace(edit.IconKey) ? null : edit.IconKey.Trim(); }
        if (edit.LegacyCode is not null) { node.LegacyCode = string.IsNullOrWhiteSpace(edit.LegacyCode) ? null : edit.LegacyCode.Trim(); }
        if (edit.Route is not null) { node.Route = string.IsNullOrWhiteSpace(edit.Route) ? null : edit.Route.Trim(); }
        if (edit.Description is not null) { node.Description = string.IsNullOrWhiteSpace(edit.Description) ? null : edit.Description.Trim(); }
        if (edit.HelpText is not null) { node.HelpText = string.IsNullOrWhiteSpace(edit.HelpText) ? null : edit.HelpText.Trim(); }
        if (edit.State is MenuNodeState s) { node.State = s; }
        if (edit.IsProcessGroup is bool ipg) { node.IsProcessGroup = ipg; }

        await _db.SaveChangesAsync(cancellationToken);
        return MenuConfigResult<MenuEditorNodeDto>.Ok(ToEditorDto(node));
    }

    public async Task<MenuConfigResult<MenuEditorNodeDto>> ToggleNodeVisibilityAsync(
        Guid nodeId, CancellationToken cancellationToken = default)
    {
        var node = await _db.MenuNodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return MenuConfigResult<MenuEditorNodeDto>.NotFound("El elemento no existe.");
        }
        node.IsVisible = !node.IsVisible;
        await _db.SaveChangesAsync(cancellationToken);
        return MenuConfigResult<MenuEditorNodeDto>.Ok(ToEditorDto(node));
    }

    public async Task<MenuConfigResult<MenuEditorNodeDto>> SetNodeStateAsync(
        Guid nodeId, MenuNodeState state, CancellationToken cancellationToken = default)
    {
        var node = await _db.MenuNodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return MenuConfigResult<MenuEditorNodeDto>.NotFound("El elemento no existe.");
        }
        node.State = state;
        await _db.SaveChangesAsync(cancellationToken);
        return MenuConfigResult<MenuEditorNodeDto>.Ok(ToEditorDto(node));
    }

    public async Task<MenuConfigResult<bool>> MoveNodeAsync(
        Guid nodeId, Guid? newParentId, int newSortOrder, CancellationToken cancellationToken = default)
    {
        var node = await _db.MenuNodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return MenuConfigResult<bool>.NotFound("El elemento no existe.");
        }

        // Todos los nodos de la vista (para validar ciclos y coherencia de Kind, y reordenar).
        var viewNodes = await _db.MenuNodes
            .Where(n => n.MenuViewId == node.MenuViewId)
            .ToListAsync(cancellationToken);

        MenuNode? newParent = null;
        if (newParentId is Guid npid)
        {
            newParent = viewNodes.FirstOrDefault(n => n.Id == npid);
            if (newParent is null)
            {
                return MenuConfigResult<bool>.NotFound("El nuevo padre no existe en esta vista.");
            }
            // Ciclo: el nuevo padre no puede ser el propio nodo ni un descendiente suyo.
            if (npid == nodeId || IsDescendant(viewNodes, ancestorId: nodeId, candidateId: npid))
            {
                return MenuConfigResult<bool>.Invalid("Movimiento invalido: crearia un ciclo en el arbol.");
            }
        }

        var kindError = MenuNodeKindRules.Validate(node.Kind, newParent?.Kind);
        if (kindError is not null)
        {
            return MenuConfigResult<bool>.Invalid(kindError);
        }

        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            node.ParentId = newParentId;

            // Reindexa a los hermanos del destino insertando el nodo en newSortOrder.
            var siblings = viewNodes
                .Where(n => n.ParentId == newParentId && n.Id != nodeId)
                .OrderBy(n => n.SortOrder).ThenBy(n => n.Name, StringComparer.Ordinal)
                .ToList();
            var clamped = Math.Clamp(newSortOrder, 0, siblings.Count);
            siblings.Insert(clamped, node);
            for (var i = 0; i < siblings.Count; i++) { siblings[i].SortOrder = i; }

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

        return MenuConfigResult<bool>.Ok(true);
    }

    public async Task<MenuConfigResult<bool>> DeleteNodeAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        var node = await _db.MenuNodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return MenuConfigResult<bool>.NotFound("El elemento no existe.");
        }

        var viewNodes = await _db.MenuNodes
            .Where(n => n.MenuViewId == node.MenuViewId)
            .ToListAsync(cancellationToken);

        // Recolecta el nodo + toda su descendencia (el self-ref es NO ACTION: hay que borrar a mano).
        var toDelete = new List<MenuNode> { node };
        var frontier = new Queue<Guid>();
        frontier.Enqueue(nodeId);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var child in viewNodes.Where(n => n.ParentId == current))
            {
                toDelete.Add(child);
                frontier.Enqueue(child.Id);
            }
        }

        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            _db.MenuNodes.RemoveRange(toDelete);
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

        return MenuConfigResult<bool>.Ok(true);
    }

    // ================= Ola 2: asignacion de usuarios =================

    public async Task<MenuConfigResult<bool>> AssignUserToViewAsync(
        Guid tenantUserId, Guid? viewId, CancellationToken cancellationToken = default)
    {
        var user = await _db.TenantUsers.FirstOrDefaultAsync(u => u.Id == tenantUserId, cancellationToken);
        if (user is null)
        {
            return MenuConfigResult<bool>.NotFound("El usuario no existe.");
        }
        if (viewId is Guid vid)
        {
            var exists = await _db.MenuViews.AnyAsync(v => v.Id == vid, cancellationToken);
            if (!exists)
            {
                return MenuConfigResult<bool>.NotFound("La vista no existe.");
            }
        }
        user.MenuViewId = viewId;
        await _db.SaveChangesAsync(cancellationToken);
        return MenuConfigResult<bool>.Ok(true);
    }

    public async Task<IReadOnlyList<TenantUserViewDto>> ListTenantUsersWithViewAsync(CancellationToken cancellationToken = default)
    {
        return await _db.TenantUsers.AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new TenantUserViewDto(
                u.Id,
                u.Email,
                u.PlatformUser != null ? u.PlatformUser.DisplayName : null,
                u.MenuViewId,
                u.MenuViewId == null ? null : _db.MenuViews.Where(v => v.Id == u.MenuViewId).Select(v => v.Name).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    // ================= Ola 2: export / import =================

    private static readonly JsonSerializerOptions ExportJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<MenuConfigResult<MenuExportDocument>> ExportViewAsync(Guid viewId, CancellationToken cancellationToken = default)
    {
        var view = await _db.MenuViews.AsNoTracking().FirstOrDefaultAsync(v => v.Id == viewId, cancellationToken);
        if (view is null)
        {
            return MenuConfigResult<MenuExportDocument>.NotFound("La vista no existe.");
        }

        var flat = await _db.MenuNodes.AsNoTracking()
            .Where(n => n.MenuViewId == viewId)
            .ToListAsync(cancellationToken);

        var childrenByParent = flat
            .Where(n => n.ParentId is not null)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        List<MenuExportNode> BuildLevel(Guid? parentId)
        {
            IEnumerable<MenuNode> level = parentId is null
                ? flat.Where(n => n.ParentId is null)
                : (childrenByParent.TryGetValue(parentId.Value, out var kids) ? kids : Enumerable.Empty<MenuNode>());
            return level
                .OrderBy(n => n.SortOrder).ThenBy(n => n.Name, StringComparer.Ordinal)
                .Select(n => new MenuExportNode(
                    n.Kind.ToString(), n.Name, n.IconKey, n.LegacyCode, n.Route,
                    n.Description, n.HelpText, n.State.ToString(), n.IsVisible, n.SortOrder,
                    BuildLevel(n.Id), n.IsProcessGroup))
                .ToList();
        }

        var doc = new MenuExportDocument(view.Name, view.Description, BuildLevel(null));
        return MenuConfigResult<MenuExportDocument>.Ok(doc);
    }

    public async Task<MenuConfigResult<MenuViewDto>> ImportViewAsync(
        string json, string newName, CancellationToken cancellationToken = default)
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

        MenuExportDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<MenuExportDocument>(json, ExportJsonOptions);
        }
        catch (JsonException)
        {
            return MenuConfigResult<MenuViewDto>.Invalid("El JSON de la vista no es valido.");
        }
        if (doc is null)
        {
            return MenuConfigResult<MenuViewDto>.Invalid("El JSON de la vista esta vacio.");
        }
        if (await _db.MenuViews.AnyAsync(v => v.Name == trimmed, cancellationToken))
        {
            return MenuConfigResult<MenuViewDto>.Conflict($"Ya existe una vista con el nombre '{trimmed}'.");
        }

        var view = new MenuView
        {
            TenantId = tenantId,
            Name = trimmed,
            Description = doc.Description,
            IsDefault = false,
            SortOrder = 0
        };

        var newNodes = new List<MenuNode>();
        void Flatten(IEnumerable<MenuExportNode> level, Guid? parentId)
        {
            var order = 0;
            foreach (var n in level)
            {
                if (!Enum.TryParse<MenuNodeKind>(n.Kind, out var kind)) { kind = MenuNodeKind.Item; }
                if (!Enum.TryParse<MenuNodeState>(n.State, out var state)) { state = MenuNodeState.Ready; }
                var node = new MenuNode
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    MenuViewId = view.Id,
                    ParentId = parentId,
                    Kind = kind,
                    Name = string.IsNullOrWhiteSpace(n.Name) ? "(sin nombre)" : n.Name.Trim(),
                    IconKey = n.IconKey,
                    LegacyCode = n.LegacyCode,
                    Route = n.Route,
                    Description = n.Description,
                    HelpText = n.HelpText,
                    State = state,
                    IsVisible = n.IsVisible,
                    SortOrder = order++,
                    IsProcessGroup = n.IsProcessGroup
                };
                newNodes.Add(node);
                if (n.Children is { Count: > 0 })
                {
                    Flatten(n.Children, node.Id);
                }
            }
        }
        Flatten(doc.Roots ?? new List<MenuExportNode>(), null);

        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            _db.MenuViews.Add(view);
            _db.MenuNodes.AddRange(newNodes);
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
            view.Id, view.Name, view.Description, view.IsDefault, view.SortOrder, newNodes.Count));
    }

    // ================= Helpers =================

    private static MenuEditorNodeDto ToEditorDto(MenuNode n) => new(
        n.Id, n.ParentId, n.Kind, n.Name, n.IconKey, n.LegacyCode, n.Route,
        n.Description, n.HelpText, n.State, n.IsVisible, n.SortOrder, n.IsProcessGroup,
        Array.Empty<MenuEditorNodeDto>());

    /// <summary>true si candidateId esta dentro del subarbol de ancestorId (para detectar ciclos).</summary>
    private static bool IsDescendant(IReadOnlyList<MenuNode> nodes, Guid ancestorId, Guid candidateId)
    {
        var current = nodes.FirstOrDefault(n => n.Id == candidateId);
        var guard = 0;
        while (current?.ParentId is Guid pid && guard++ < 1000)
        {
            if (pid == ancestorId) { return true; }
            current = nodes.FirstOrDefault(n => n.Id == pid);
        }
        return false;
    }
}
