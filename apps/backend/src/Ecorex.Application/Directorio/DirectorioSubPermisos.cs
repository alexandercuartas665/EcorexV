using Ecorex.Application.Roles;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Sub-permisos NOMBRADOS del Directorio General (modulo 000232). El modulo declara acciones
/// mas finas que las cuatro estandar (View/Create/Edit/Delete): permitir crear una Empresa,
/// crear un Cliente o crear un Sospechoso de forma independiente.
///
/// Mecanismo (de menor riesgo, sin tocar el enforcement existente): cada sub-permiso se expone
/// como una ENTRADA ADICIONAL del catalogo de la matriz de roles, con una <see cref="ModuloInfo.Key"/>
/// propia (ej. "directorio-general:crear-empresa"). Como las claves son solo strings en la matriz
/// RolPermiso y en <see cref="EffectivePermissions"/>, se resuelven igual que cualquier modulo:
/// la pagina consulta <c>Can(key, PermissionAction.Create)</c>. La accion relevante de un
/// sub-permiso es CREAR (CanCreate); las demas columnas quedan sin uso para estas filas.
///
/// Owner/Admin y usuarios sin rol siguen siendo Unrestricted (el resolver no cambia): estos
/// sub-permisos SOLO acotan a usuarios que ya tienen un rol de permisos finos asignado.
/// </summary>
public static class DirectorioSubPermisos
{
    /// <summary>Ruta/Key del modulo padre en el catalogo (coincide con el Route del menu).</summary>
    public const string ModuleRoute = "directorio-general";

    public const string CrearEmpresa = "directorio-general:crear-empresa";
    public const string CrearCliente = "directorio-general:crear-cliente";
    public const string CrearSospechoso = "directorio-general:crear-sospechoso";

    /// <summary>Grupo en el que la matriz de roles agrupa estas filas.</summary>
    public const string Grupo = "Directorio General";

    /// <summary>
    /// Entradas de sub-permiso para inyectar en el catalogo de la matriz de roles. El RolService
    /// las agrega SOLO si el catalogo ya contiene el modulo padre (para no ofrecer permisos de un
    /// modulo que el tenant no tiene en su menu).
    /// </summary>
    public static readonly IReadOnlyList<ModuloInfo> Entradas = new List<ModuloInfo>
    {
        new(CrearEmpresa, "Crear empresa", Grupo),
        new(CrearCliente, "Crear cliente", Grupo),
        new(CrearSospechoso, "Crear sospechoso", Grupo),
    };

    /// <summary>Solo las claves (para validaciones / resolucion).</summary>
    public static readonly IReadOnlySet<string> Keys =
        new HashSet<string>(StringComparer.Ordinal) { CrearEmpresa, CrearCliente, CrearSospechoso };
}
