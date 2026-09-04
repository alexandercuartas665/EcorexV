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

    /// <summary>Estructura completa del motor (todas las secciones "mod_" con sus campos y las areas del
    /// catalogo) para el modal "Configurar directorio". Solo lectura.</summary>
    Task<ModularEstructuraDto> GetEstructuraAsync(CancellationToken cancellationToken = default);

    /// <summary>Crea un tercero desde el motor Modular: deduce la naturaleza de lo que se lleno, estampa
    /// DirectoryEngine=Modular, guarda los valores en FichasJson y lo asigna a la categoria. Devuelve el
    /// id del tercero creado, o un mensaje de error.</summary>
    Task<(Guid? Id, string? Error)> CreateTerceroAsync(CreateModularTerceroRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lee un tercero Modular para editarlo (su categoria, estado y valores guardados). Null si no
    /// existe o no es del motor Modular.</summary>
    Task<ModularEditDto?> GetTerceroParaEditarAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Actualiza un tercero Modular existente: re-deduce nombre/naturaleza/campos base de los
    /// valores, guarda FichasJson y el estado. Devuelve un mensaje de error o null si OK.</summary>
    Task<string?> UpdateTerceroAsync(Guid id, CreateModularTerceroRequest request, string estado, CancellationToken cancellationToken = default);
}
