using Ecorex.Application.Roles;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Permisos de AREA del Directorio Modular (Capa 8, regla 5, O5-1). Cada una de las cuatro areas del
/// motor (Administracion / Comercial / Contabilidad / Logistica) se expone como una fila propia en la
/// matriz de roles (misma mecanica que <see cref="DirectorioSubPermisos"/>): una clave string
/// "directorio-modular:area:{key}" resoluble por <c>EffectivePermissions.Can(key, View)</c>.
///
/// Asi el "area del usuario" se MAPEA DESDE EL ROL de permisos: un rol concede las areas cuyas filas
/// tenga marcadas en Ver. Owner/Admin y usuarios SIN rol siguen Unrestricted (ven todo, back-compat);
/// un rol que no marque NINGUNA area se trata como "no configurado" y tampoco restringe (opt-in), para
/// no bloquear a nadie hasta que el admin configure las areas.
/// </summary>
public static class DirectorioModularAreaPermisos
{
    public const string Prefix = "directorio-modular:area:";
    public const string Grupo = "Directorio Modular - Areas";

    /// <summary>Las cuatro areas del motor (key -> etiqueta), en el orden del catalogo.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Areas = new List<(string, string)>
    {
        ("admin", "Administracion"),
        ("comercial", "Comercial"),
        ("contabilidad", "Contabilidad"),
        ("logistica", "Logistica"),
    };

    /// <summary>Clave de permiso de una area (ej. "directorio-modular:area:comercial").</summary>
    public static string PermKey(string areaKey) => Prefix + areaKey;

    /// <summary>Filas para inyectar en el catalogo de la matriz de roles (la accion util es Ver).</summary>
    public static readonly IReadOnlyList<ModuloInfo> Entradas =
        Areas.Select(a => new ModuloInfo(PermKey(a.Key), $"Ver area {a.Label}", Grupo)).ToList();

    /// <summary>Solo las claves de permiso (para detectar si un rol configuro alguna area).</summary>
    public static readonly IReadOnlySet<string> Keys =
        new HashSet<string>(Areas.Select(a => PermKey(a.Key)), StringComparer.Ordinal);
}
