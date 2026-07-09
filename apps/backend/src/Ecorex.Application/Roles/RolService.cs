using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Roles;

/// <summary>
/// Implementacion de IRolService (Ola B1, ADR-0032). Aislamiento por tenant via filtro global.
/// Auditoria en cada mutacion (AdminAuditLog / super_admin_audit_logs) dentro del SaveChanges.
/// La matriz de permisos se guarda borrando e reinsertando en transaccion (solo filas con flag).
/// </summary>
public sealed class RolService : IRolService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;

    public RolService(IApplicationDbContext db, ITenantContext tenant, IAuditWriter audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<IReadOnlyList<RolDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        // UserCount por rol via subconsulta (el filtro global limita ambos lados al tenant).
        return await _db.Roles.AsNoTracking()
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .Select(r => new RolDto(
                r.Id, r.Name, r.Description, r.IsActive, r.IsSystem,
                _db.TenantUsers.Count(u => u.RolId == r.Id)))
            .ToListAsync(cancellationToken);
    }

    public async Task<RolDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var r = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (r is null) { return null; }
        var permisos = await _db.RolPermisos.AsNoTracking().Where(p => p.RolId == id)
            .Select(p => new ModulePermissionDto(p.ModuleKey, p.CanView, p.CanCreate, p.CanEdit, p.CanDelete))
            .ToListAsync(cancellationToken);
        return new RolDetailDto(r.Id, r.Name, r.Description, r.IsActive, r.IsSystem, permisos);
    }

    public async Task<RolResult<RolDto>> SaveAsync(
        Guid? id, string name, string? description, bool isActive, Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return RolResult<RolDto>.Invalid("El nombre del rol es obligatorio.");
        }
        if (trimmed.Length > 150)
        {
            return RolResult<RolDto>.Invalid("El nombre no puede superar 150 caracteres.");
        }

        Rol entity;
        if (id is Guid gid)
        {
            var found = await _db.Roles.FirstOrDefaultAsync(x => x.Id == gid, cancellationToken);
            if (found is null)
            {
                return RolResult<RolDto>.NotFound("El rol no existe.");
            }
            entity = found;
            if (entity.IsSystem && !string.Equals(entity.Name, trimmed, StringComparison.Ordinal))
            {
                return RolResult<RolDto>.Invalid("El rol de sistema no se puede renombrar.");
            }
            if (await _db.Roles.AnyAsync(x => x.Name == trimmed && x.Id != gid, cancellationToken))
            {
                return RolResult<RolDto>.Conflict($"Ya existe un rol con el nombre '{trimmed}'.");
            }

            var prev = new { entity.Name, entity.Description, entity.IsActive };
            entity.Name = trimmed;
            entity.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            entity.IsActive = isActive;
            _audit.Write(actorUserId, "rol.update", nameof(Rol), entity.Id,
                previousValue: prev,
                newValue: new { entity.Name, entity.Description, entity.IsActive },
                tenantId: entity.TenantId);
        }
        else
        {
            if (_tenant.TenantId is not Guid tenantId)
            {
                return RolResult<RolDto>.Invalid("No hay tenant activo.");
            }
            if (await _db.Roles.AnyAsync(x => x.Name == trimmed, cancellationToken))
            {
                return RolResult<RolDto>.Conflict($"Ya existe un rol con el nombre '{trimmed}'.");
            }
            entity = new Rol
            {
                TenantId = tenantId,
                Name = trimmed,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                IsActive = isActive,
                IsSystem = false
            };
            _db.Roles.Add(entity);
            _audit.Write(actorUserId, "rol.create", nameof(Rol), entity.Id,
                previousValue: null,
                newValue: new { entity.Name, entity.Description, entity.IsActive },
                tenantId: entity.TenantId);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return RolResult<RolDto>.Ok(new RolDto(
            entity.Id, entity.Name, entity.Description, entity.IsActive, entity.IsSystem,
            await _db.TenantUsers.CountAsync(u => u.RolId == entity.Id, cancellationToken)));
    }

    public async Task<RolResult<bool>> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Roles.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return RolResult<bool>.NotFound("El rol no existe.");
        }
        if (entity.IsSystem)
        {
            return RolResult<bool>.Invalid("No se puede eliminar el rol de sistema.");
        }
        var users = await _db.TenantUsers.CountAsync(u => u.RolId == id, cancellationToken);
        if (users > 0)
        {
            return RolResult<bool>.Invalid(
                $"No se puede eliminar: el rol tiene {users} usuario(s) asignado(s). Reasigna esos usuarios primero.");
        }

        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            var permisos = await _db.RolPermisos.Where(p => p.RolId == id).ToListAsync(cancellationToken);
            _db.RolPermisos.RemoveRange(permisos);
            _db.Roles.Remove(entity);
            _audit.Write(actorUserId, "rol.delete", nameof(Rol), entity.Id,
                previousValue: new { entity.Name },
                newValue: null,
                tenantId: entity.TenantId);
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

        return RolResult<bool>.Ok(true);
    }

    public async Task<RolResult<bool>> SavePermisosAsync(
        Guid rolId, IReadOnlyList<ModulePermissionDto> permisos, Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var rol = await _db.Roles.FirstOrDefaultAsync(x => x.Id == rolId, cancellationToken);
        if (rol is null)
        {
            return RolResult<bool>.NotFound("El rol no existe.");
        }

        // Solo se persisten las filas con al menos un flag (logica pura, testeable).
        var toPersist = PermissionResolver.FilterPersistable(permisos);

        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await _db.RolPermisos.Where(p => p.RolId == rolId).ToListAsync(cancellationToken);
            _db.RolPermisos.RemoveRange(existing);
            foreach (var p in toPersist)
            {
                _db.RolPermisos.Add(new RolPermiso
                {
                    TenantId = rol.TenantId,
                    RolId = rolId,
                    ModuleKey = p.ModuleKey,
                    CanView = p.CanView,
                    CanCreate = p.CanCreate,
                    CanEdit = p.CanEdit,
                    CanDelete = p.CanDelete
                });
            }
            _audit.Write(actorUserId, "rol.save-permisos", nameof(Rol), rolId,
                previousValue: new { Count = existing.Count },
                newValue: new { Count = toPersist.Count },
                tenantId: rol.TenantId);
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

        return RolResult<bool>.Ok(true);
    }

    public async Task<IReadOnlyList<ModuloInfo>> GetModuleCatalogAsync(CancellationToken cancellationToken = default)
    {
        // Vista IsDefault del tenant (o la primera si ninguna esta marcada).
        var defaultView = await _db.MenuViews.AsNoTracking()
            .Where(v => v.IsDefault)
            .OrderBy(v => v.SortOrder).ThenBy(v => v.Name)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _db.MenuViews.AsNoTracking()
                .OrderBy(v => v.SortOrder).ThenBy(v => v.Name)
                .FirstOrDefaultAsync(cancellationToken);

        if (defaultView is null)
        {
            return WithSubPermisos(ModuleCatalogFallback.Modules);
        }

        var nodes = await _db.MenuNodes.AsNoTracking()
            .Where(n => n.MenuViewId == defaultView.Id)
            .Select(n => new { n.Id, n.ParentId, n.Kind, n.Name, n.Route, n.State, n.SortOrder })
            .ToListAsync(cancellationToken);

        var byId = nodes.ToDictionary(n => n.Id);

        // Nombre del grupo = Section ancestro del Item (subiendo por ParentId a traves de Subgroups).
        string GroupNameFor(Guid? parentId)
        {
            var guard = 0;
            var current = parentId;
            while (current is Guid pid && byId.TryGetValue(pid, out var node) && guard++ < 100)
            {
                if (node.Kind == MenuNodeKind.Section)
                {
                    return node.Name;
                }
                current = node.ParentId;
            }
            return "Sin grupo";
        }

        var catalog = nodes
            .Where(n => n.Kind == MenuNodeKind.Item
                && n.State == MenuNodeState.Ready
                && !string.IsNullOrWhiteSpace(n.Route))
            // Un modulo real = un Route unico (varios items pueden reusar la misma pagina;
            // gana el primero por orden de aparicion en el menu).
            .GroupBy(n => n.Route!, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.OrderBy(x => x.SortOrder).First();
                return new ModuloInfo(first.Route!, first.Name, GroupNameFor(first.ParentId));
            })
            .OrderBy(m => m.Grupo, StringComparer.Ordinal).ThenBy(m => m.Label, StringComparer.Ordinal)
            .ToList();

        return WithSubPermisos(catalog.Count > 0 ? catalog : ModuleCatalogFallback.Modules);
    }

    /// <summary>
    /// Agrega los sub-permisos NOMBRADOS de modulos que los declaran (hoy: Directorio General,
    /// 000232) al catalogo de la matriz de roles, SOLO si el modulo padre esta presente. Asi la
    /// matriz puede listar acciones finas (crear-empresa / crear-cliente / crear-sospechoso) como
    /// filas propias, resolubles por EffectivePermissions.Can(key, Create). No altera el
    /// enforcement: Owner/Admin y usuarios sin rol siguen Unrestricted.
    /// </summary>
    private static IReadOnlyList<ModuloInfo> WithSubPermisos(IReadOnlyList<ModuloInfo> catalog)
    {
        var tieneDirectorio = catalog.Any(m =>
            string.Equals(m.Key, Directorio.DirectorioSubPermisos.ModuleRoute, StringComparison.Ordinal));
        if (!tieneDirectorio)
        {
            return catalog;
        }
        var extended = new List<ModuloInfo>(catalog);
        extended.AddRange(Directorio.DirectorioSubPermisos.Entradas);
        return extended;
    }

    public async Task<EffectivePermissions> ResolveEffectivePermissionsAsync(
        Guid platformUserId, CancellationToken cancellationToken = default)
    {
        var tu = await _db.TenantUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.PlatformUserId == platformUserId, cancellationToken);
        if (tu is null)
        {
            // Sin TenantUser resoluble: no hay matriz que aplicar -> Unrestricted (back-compat).
            return EffectivePermissions.UnrestrictedAccess();
        }

        var isOwnerOrAdmin = tu.TenantRole is TenantRole.Owner or TenantRole.Admin;
        if (isOwnerOrAdmin)
        {
            return PermissionResolver.Resolve(true, null, null);
        }
        if (tu.RolId is not Guid rolId)
        {
            // Usuario sin rol de permisos finos: conserva el acceso del paso 1 (regla opt-in B2).
            return EffectivePermissions.UnrestrictedAccess();
        }

        var permisos = await _db.RolPermisos.AsNoTracking().Where(p => p.RolId == rolId)
            .Select(p => new ModulePermissionDto(p.ModuleKey, p.CanView, p.CanCreate, p.CanEdit, p.CanDelete))
            .ToListAsync(cancellationToken);
        return PermissionResolver.Resolve(false, rolId, permisos);
    }

    public async Task<RolResult<bool>> AssignRoleToUserAsync(
        Guid tenantUserId, Guid? rolId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var user = await _db.TenantUsers.FirstOrDefaultAsync(u => u.Id == tenantUserId, cancellationToken);
        if (user is null)
        {
            return RolResult<bool>.NotFound("El usuario no existe.");
        }
        if (rolId is Guid rid)
        {
            var exists = await _db.Roles.AnyAsync(r => r.Id == rid, cancellationToken);
            if (!exists)
            {
                return RolResult<bool>.NotFound("El rol no existe.");
            }
        }

        var previous = user.RolId;
        if (previous != rolId)
        {
            user.RolId = rolId;
            _audit.Write(actorUserId, "tenant-user.assign-rol", nameof(TenantUser), user.Id,
                previousValue: new { RolId = previous },
                newValue: new { RolId = rolId },
                tenantId: user.TenantId);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return RolResult<bool>.Ok(true);
    }
}

/// <summary>
/// Catalogo minimo de modulos usado solo cuando el tenant aun no tiene menu configurado
/// (defensa: la matriz nunca queda vacia). En operacion normal el catalogo sale del menu real.
/// </summary>
public static class ModuleCatalogFallback
{
    public static readonly IReadOnlyList<ModuloInfo> Modules = new List<ModuloInfo>
    {
        new("actividades", "Administrar actividades", "Mis Procesos"),
        new("proyectos", "Proyectos", "Mis Procesos"),
        new("inventario-items", "Items de inventarios", "Sistema · Inventarios"),
        new("flujos", "Flujos del proceso", "Automatizacion"),
        new("formularios", "Formularios", "Automatizacion"),
        new("admin-usuarios", "Administracion de usuarios", "Sistema · General"),
        new("roles-permisos", "Roles y permisos", "Sistema · General"),
    };
}
