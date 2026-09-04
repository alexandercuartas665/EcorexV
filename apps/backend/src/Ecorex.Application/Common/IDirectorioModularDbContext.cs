using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Common;

/// <summary>
/// Frontera de datos del 2do motor de contactos ("Directorio Modular", Capa 8). Interfaz SEGREGADA
/// (ISP) que expone solo las entidades propias del motor Modular, para no tocar
/// <see cref="IApplicationDbContext"/> (y sus muchos fakes de test). La implementa el mismo
/// <c>EcorexDbContext</c> (registrado como la misma instancia scoped que IApplicationDbContext, asi que
/// comparten unidad de trabajo). Las entidades COMPARTIDAS con el motor Clasico (Terceros, fichas y
/// campos) se siguen leyendo por IApplicationDbContext.
/// </summary>
public interface IDirectorioModularDbContext
{
    DbSet<DirectorioCategoria> DirectorioCategorias { get; }
    DbSet<DirectorioCategoriaSeccion> DirectorioCategoriaSecciones { get; }
    DbSet<TerceroCategoria> TerceroCategorias { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
