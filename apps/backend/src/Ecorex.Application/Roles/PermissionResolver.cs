namespace Ecorex.Application.Roles;

/// <summary>
/// Set de permisos de un modulo (una celda de la matriz resuelta a booleans).
/// </summary>
public sealed record ModuleAccess(bool View, bool Create, bool Edit, bool Delete)
{
    public static readonly ModuleAccess None = new(false, false, false, false);
    public static readonly ModuleAccess All = new(true, true, true, true);

    public bool Can(PermissionAction action) => action switch
    {
        PermissionAction.View => View,
        PermissionAction.Create => Create,
        PermissionAction.Edit => Edit,
        PermissionAction.Delete => Delete,
        _ => false
    };
}

/// <summary>
/// Permisos efectivos resueltos de un usuario del tenant (listo para el enforcement de Ola B2 y
/// para la UI). AllowAll = manda por poder organico (Owner/Admin) y puede todo, sin mirar el rol.
/// Si no es AllowAll, cada modulo se resuelve por su fila en el rol; un modulo ausente = sin acceso.
/// Estructura inmutable y sin dependencias de EF (logica pura, testeable).
/// </summary>
public sealed class EffectivePermissions
{
    private readonly IReadOnlyDictionary<string, ModuleAccess> _byModule;

    public bool AllowAll { get; }

    /// <summary>Id del rol de permisos aplicado (null si AllowAll o sin rol).</summary>
    public Guid? RolId { get; }

    private EffectivePermissions(bool allowAll, Guid? rolId, IReadOnlyDictionary<string, ModuleAccess> byModule)
    {
        AllowAll = allowAll;
        RolId = rolId;
        _byModule = byModule;
    }

    /// <summary>Owner/Admin: acceso total, sin importar el rol de permisos.</summary>
    public static EffectivePermissions AllowAllPermissions() =>
        new(true, null, EmptyMap);

    /// <summary>Usuario sin rol de permisos: acceso vacio (todo denegado).</summary>
    public static EffectivePermissions Empty() =>
        new(false, null, EmptyMap);

    /// <summary>Usuario con rol: resuelve el set desde sus filas de permiso.</summary>
    public static EffectivePermissions FromPermissions(Guid rolId, IEnumerable<ModulePermissionDto> permisos)
    {
        var map = new Dictionary<string, ModuleAccess>(StringComparer.Ordinal);
        foreach (var p in permisos)
        {
            if (string.IsNullOrWhiteSpace(p.ModuleKey)) { continue; }
            map[p.ModuleKey] = new ModuleAccess(p.CanView, p.CanCreate, p.CanEdit, p.CanDelete);
        }
        return new EffectivePermissions(false, rolId, map);
    }

    /// <summary>Set del modulo (None si AllowAll no aplica y el modulo no esta en el rol).</summary>
    public ModuleAccess For(string moduleKey)
    {
        if (AllowAll) { return ModuleAccess.All; }
        if (moduleKey is null) { return ModuleAccess.None; }
        return _byModule.TryGetValue(moduleKey, out var access) ? access : ModuleAccess.None;
    }

    /// <summary>Helper de conveniencia para el enforcement (Ola B2) y la UI.</summary>
    public bool Can(string moduleKey, PermissionAction action)
    {
        if (AllowAll) { return true; }
        return For(moduleKey).Can(action);
    }

    /// <summary>Modulos con al menos un permiso (para depurar / mostrar). Vacio si AllowAll.</summary>
    public IReadOnlyCollection<string> ModuleKeys => (IReadOnlyCollection<string>)_byModule.Keys;

    private static readonly IReadOnlyDictionary<string, ModuleAccess> EmptyMap =
        new Dictionary<string, ModuleAccess>(StringComparer.Ordinal);
}

/// <summary>
/// Logica pura de resolucion y de filtrado de permisos (sin EF, testeable en Application.Tests).
/// El servicio la usa para no repetir reglas y para poder probarlas en aislamiento.
/// </summary>
public static class PermissionResolver
{
    /// <summary>
    /// Filtra las filas de permiso que deben persistirse: solo las que tienen al menos un flag en
    /// true (SavePermisos borra e reinserta). Deduplica por ModuleKey (gana la ultima). Descarta
    /// las que no correspondan a un modulo del catalogo si <paramref name="validModuleKeys"/> se
    /// provee (null = no valida contra catalogo).
    /// </summary>
    public static IReadOnlyList<ModulePermissionDto> FilterPersistable(
        IEnumerable<ModulePermissionDto> permisos,
        ISet<string>? validModuleKeys = null)
    {
        var byKey = new Dictionary<string, ModulePermissionDto>(StringComparer.Ordinal);
        foreach (var p in permisos)
        {
            if (p is null || string.IsNullOrWhiteSpace(p.ModuleKey)) { continue; }
            if (!p.HasAny) { continue; }
            if (validModuleKeys is not null && !validModuleKeys.Contains(p.ModuleKey)) { continue; }
            byKey[p.ModuleKey] = p;
        }
        return byKey.Values.ToList();
    }

    /// <summary>
    /// Resuelve el set efectivo dado el poder organico (isOwnerOrAdmin), el rol asignado y sus
    /// permisos. Owner/Admin -> AllowAll; con rol -> set del rol; sin rol -> vacio.
    /// </summary>
    public static EffectivePermissions Resolve(
        bool isOwnerOrAdmin,
        Guid? rolId,
        IEnumerable<ModulePermissionDto>? permisos)
    {
        if (isOwnerOrAdmin)
        {
            return EffectivePermissions.AllowAllPermissions();
        }
        if (rolId is Guid id && permisos is not null)
        {
            return EffectivePermissions.FromPermissions(id, permisos);
        }
        return EffectivePermissions.Empty();
    }
}
