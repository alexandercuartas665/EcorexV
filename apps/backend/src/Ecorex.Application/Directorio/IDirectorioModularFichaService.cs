namespace Ecorex.Application.Directorio;

/// <summary>
/// Ficha por SECCIONES del motor Modular (Capa 8): arma la ficha de una categoria (para el modal de
/// crear/editar) y crea el tercero estampando el motor. Lee las secciones/campos "mod_" (que el motor
/// Clasico oculta) via composicion de la categoria. Tenant-scoped por el filtro global.
/// </summary>
public interface IDirectorioModularFichaService
{
    /// <summary>La ficha que arma la categoria: secciones (en orden de composicion) con sus campos.</summary>
    Task<ModularFichaDto?> GetFichaAsync(string categoriaKey, CancellationToken cancellationToken = default);

    /// <summary>Crea un tercero desde el motor Modular: deduce la naturaleza de lo que se lleno, estampa
    /// DirectoryEngine=Modular, guarda los valores en FichasJson y lo asigna a la categoria. Devuelve el
    /// id del tercero creado, o un mensaje de error.</summary>
    Task<(Guid? Id, string? Error)> CreateTerceroAsync(CreateModularTerceroRequest request, CancellationToken cancellationToken = default);
}
