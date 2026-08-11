namespace Ecorex.Application.Catalogos;

/// <summary>Ciudad del catalogo global (municipio DANE de Colombia).</summary>
public sealed record CiudadDto(Guid Id, string Nombre, string? Departamento, string? CodigoDane);

/// <summary>
/// Catalogo GLOBAL de ciudades / municipios (compartido por todos los tenants). Provee el listado
/// completo y la busqueda por termino para el autocompletar del selector de ciudad (Directorio
/// General y modal de Tercero). Solo lectura desde la UI: la siembra la hace el DatabaseSeeder.
/// </summary>
public interface ICiudadCatalogService
{
    /// <summary>Todas las ciudades del catalogo, ordenadas por nombre.</summary>
    Task<IReadOnlyList<CiudadDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ciudades cuyo nombre (o departamento) contiene <paramref name="term"/>. Con termino vacio
    /// devuelve las primeras <paramref name="take"/> por nombre. Case-insensitive.
    /// </summary>
    Task<IReadOnlyList<CiudadDto>> SearchAsync(string? term, int take = 30, CancellationToken cancellationToken = default);

    /// <summary>Departamentos distintos del catalogo (para la cascada geografica), ordenados.</summary>
    Task<IReadOnlyList<string>> ListDepartamentosAsync(CancellationToken cancellationToken = default);

    /// <summary>Municipios de un departamento (para la cascada geografica), ordenados por nombre.</summary>
    Task<IReadOnlyList<string>> ListMunicipiosAsync(string departamento, CancellationToken cancellationToken = default);
}
