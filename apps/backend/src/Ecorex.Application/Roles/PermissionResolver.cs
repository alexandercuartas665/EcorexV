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
/// para la UI). Dos ejes de "acceso total":
/// - <see cref="AllowAll"/> = manda por poder organico (Owner/Admin): puede todo por gobierno.
/// - <see cref="Unrestricted"/> = usuario SIN rol de permisos finos: no hay matriz que aplicar, asi
///   que conserva el comportamiento anterior (acceso como en el paso 1, back-compat opt-in). Owner/
///   Admin tambien son Unrestricted por definicion (AllowAll implica Unrestricted).
/// Si NO es Unrestricted, cada modulo se resuelve por su fila en el rol; un modulo ausente = sin
/// acceso. Estructura inmutable y sin dependencias de EF (logica pura, testeable).
/// </summary>
public sealed class EffectivePermissions
{
    private readonly IReadOnlyDictionary<string, ModuleAccess> _byModule;

    /// <summary>Acceso total por poder organico (Owner/Admin del tenant). Implica <see cref="Unrestricted"/>.</summary>
    public bool AllowAll { get; }

    /// <summary>
    /// No hay matriz de permisos que aplicar a este usuario (Owner/Admin, o usuario sin RolId): el
    /// enforcement NO restringe (fail-open / back-compat). Solo un usuario CON rol queda sujeto a su
    /// matriz. Todo <see cref="AllowAll"/> es Unrestricted, pero no al reves (sin-rol es Unrestricted
    /// sin ser AllowAll: no ostenta poder organico pero tampoco tiene rol que lo limite).
    /// </summary>
    public bool Unrestricted { get; }

    /// <summary>Id del rol de permisos aplicado (null si AllowAll, Unrestricted sin rol o sin rol).</summary>
    public Guid? RolId { get; }

    private EffectivePermissions(bool allowAll, bool unrestricted, Guid? rolId, IReadOnlyDictionary<string, ModuleAccess> byModule)
    {
        AllowAll = allowAll;
        Unrestricted = unrestricted;
        RolId = rolId;
        _byModule = byModule;
    }

    /// <summary>Owner/Admin: acceso total por gobierno, sin importar el rol de permisos.</summary>
    public static EffectivePermissions AllowAllPermissions() =>
        new(true, true, null, EmptyMap);

    /// <summary>
    /// Usuario sin rol de permisos finos (o sin TenantUser resoluble): SIN restriccion. Conserva el
    /// comportamiento del paso 1 para no bloquear a nadie que hoy no tiene rol (regla opt-in de B2).
    /// </summary>
    public static EffectivePermissions UnrestrictedAccess() =>
        new(false, true, null, EmptyMap);

    /// <summary>Usuario con rol: resuelve el set desde sus filas de permiso (queda sujeto a la matriz).</summary>
    public static EffectivePermissions FromPermissions(Guid rolId, IEnumerable<ModulePermissionDto> permisos)
    {
        var map = new Dictionary<string, ModuleAccess>(StringComparer.Ordinal);
        foreach (var p in permisos)
        {
            if (string.IsNullOrWhiteSpace(p.ModuleKey)) { continue; }
            map[p.ModuleKey] = new ModuleAccess(p.CanView, p.CanCreate, p.CanEdit, p.CanDelete);
        }
        return new EffectivePermissions(false, false, rolId, map);
    }

    /// <summary>Set del modulo (All si Unrestricted; None si con rol y el modulo no esta en la matriz).</summary>
    public ModuleAccess For(string moduleKey)
    {
        if (Unrestricted) { return ModuleAccess.All; }
        if (moduleKey is null) { return ModuleAccess.None; }
        return _byModule.TryGetValue(moduleKey, out var access) ? access : ModuleAccess.None;
    }

    /// <summary>Helper de conveniencia para el enforcement (Ola B2) y la UI.</summary>
    public bool Can(string moduleKey, PermissionAction action)
    {
        if (Unrestricted) { return true; }
        return For(moduleKey).Can(action);
    }

    /// <summary>Modulos con al menos un permiso (para depurar / mostrar). Vacio si Unrestricted.</summary>
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
    /// permisos. Owner/Admin -> AllowAll; con rol -> set del rol; SIN rol -> Unrestricted (regla
    /// opt-in de la Ola B2: quien no tiene rol conserva el acceso del paso 1, no se bloquea).
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
        return EffectivePermissions.UnrestrictedAccess();
    }
}
