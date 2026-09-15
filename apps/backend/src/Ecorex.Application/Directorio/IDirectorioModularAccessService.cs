using Ecorex.Application.Common;
using Ecorex.Application.Roles;

namespace Ecorex.Application.Directorio;

/// <summary>Acceso por AREA del usuario actual al Directorio Modular (Capa 8, O5-1). VeTodo = sin
/// restriccion (Owner/Admin, sin rol, o rol que no configuro areas); si no, <see cref="Areas"/> son las
/// areas que su rol le concede.</summary>
public sealed record ModularAreaAccess(bool VeTodo, IReadOnlySet<string> Areas)
{
    /// <summary>El usuario ve algo etiquetado con estas areas? Sin areas (null/vacio) => visible a todos.</summary>
    public bool PuedeArea(string? areasCsv)
    {
        if (VeTodo) { return true; }
        if (string.IsNullOrWhiteSpace(areasCsv)) { return true; }
        foreach (var a in areasCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Areas.Contains(a)) { return true; }
        }
        return false;
    }
}

/// <summary>Resuelve las areas del Directorio Modular que puede ver el usuario actual, mapeadas desde su
/// rol de permisos (regla 5, O5-1).</summary>
public interface IDirectorioModularAccessService
{
    Task<ModularAreaAccess> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class DirectorioModularAccessService : IDirectorioModularAccessService
{
    private static readonly IReadOnlySet<string> Vacio = new HashSet<string>(StringComparer.Ordinal);

    private readonly IRolService _roles;
    private readonly ITenantContext _tenant;

    public DirectorioModularAccessService(IRolService roles, ITenantContext tenant)
    {
        _roles = roles;
        _tenant = tenant;
    }

    public async Task<ModularAreaAccess> GetAsync(CancellationToken cancellationToken = default)
    {
        // Sin usuario resoluble: fail-open (no bloquea; el aislamiento por tenant ya aplica).
        if (_tenant.UserId is not Guid uid) { return new ModularAreaAccess(true, Vacio); }

        var eff = await _roles.ResolveEffectivePermissionsAsync(uid, cancellationToken);
        return Resolve(eff);
    }

    /// <summary>Deriva el acceso por area desde los permisos efectivos (logica PURA, testeable). Owner/Admin
    /// o sin rol => VeTodo; con rol que NO marco ninguna area => VeTodo (opt-in); si no, las areas marcadas.</summary>
    public static ModularAreaAccess Resolve(EffectivePermissions eff)
    {
        if (eff.Unrestricted) { return new ModularAreaAccess(true, Vacio); }

        // Con rol: si NO marco ninguna fila de area, se trata como "no configurado" -> ve todo (opt-in).
        var configuro = eff.ModuleKeys.Any(k => DirectorioModularAreaPermisos.Keys.Contains(k));
        if (!configuro) { return new ModularAreaAccess(true, Vacio); }

        var areas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, _) in DirectorioModularAreaPermisos.Areas)
        {
            if (eff.Can(DirectorioModularAreaPermisos.PermKey(key), PermissionAction.View)) { areas.Add(key); }
        }
        return new ModularAreaAccess(false, areas);
    }
}
