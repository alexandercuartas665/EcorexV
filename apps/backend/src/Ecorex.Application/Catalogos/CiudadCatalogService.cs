using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Catalogos;

/// <summary>
/// Implementacion de ICiudadCatalogService sobre el catalogo GLOBAL de ciudades. La entidad Ciudad
/// no es tenant-scoped, asi que no lleva filtro de tenant: el mismo listado sirve a todos.
/// </summary>
public sealed class CiudadCatalogService : ICiudadCatalogService
{
    private readonly IApplicationDbContext _db;

    public CiudadCatalogService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CiudadDto>> ListAsync(CancellationToken cancellationToken = default)
        => await _db.Ciudades
            .AsNoTracking()
            .OrderBy(c => c.Nombre)
            .Select(c => new CiudadDto(c.Id, c.Nombre, c.Departamento, c.CodigoDane))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CiudadDto>> SearchAsync(string? term, int take = 30, CancellationToken cancellationToken = default)
    {
        if (take <= 0) { take = 30; }
        var q = _db.Ciudades.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(term))
        {
            // Provider-neutral case-insensitive (mismo patron que TerceroService): ToLower().Contains
            // se traduce a SQL en Postgres y SQL Server sin depender de operadores especificos.
            var t = term.Trim().ToLower();
            q = q.Where(c => c.Nombre.ToLower().Contains(t)
                             || (c.Departamento != null && c.Departamento.ToLower().Contains(t)));
        }

        return await q
            .OrderBy(c => c.Nombre)
            .Take(take)
            .Select(c => new CiudadDto(c.Id, c.Nombre, c.Departamento, c.CodigoDane))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListDepartamentosAsync(CancellationToken cancellationToken = default)
        => await _db.Ciudades.AsNoTracking()
            .Where(c => c.Departamento != null && c.Departamento != "")
            .Select(c => c.Departamento!)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> ListMunicipiosAsync(string departamento, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(departamento)) { return Array.Empty<string>(); }
        var dep = departamento.Trim();
        return await _db.Ciudades.AsNoTracking()
            .Where(c => c.Departamento == dep)
            .OrderBy(c => c.Nombre)
            .Select(c => c.Nombre)
            .ToListAsync(cancellationToken);
    }
}
