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

    /// <summary>Cuantos terceros de nivel raiz del motor Clasico existen (candidatos a migrar al Modular).</summary>
    Task<int> CountClasicoAsync(CancellationToken cancellationToken = default);

    /// <summary>Exporta todos los terceros del motor Modular a un .xlsx con la misma estructura que la
    /// plantilla de importacion (re-importable). Devuelve los bytes del archivo.</summary>
    Task<byte[]> ExportXlsxAsync(CancellationToken cancellationToken = default);

    /// <summary>Alta en lote desde la plantilla de importacion (reusa el parser del Directorio basico):
    /// crea cada fila valida como tercero del motor Modular, en la categoria dada, mapeando los campos base
    /// a la seccion publica. Devuelve cuantas se crearon y cuantas fallaron.</summary>
    Task<(int Done, int Failed)> ImportAsync(string categoriaKey, IReadOnlyList<TerceroImportXlsx.TerceroImportRow> rows, CancellationToken cancellationToken = default);

    /// <summary>Migra TODOS los terceros del motor Clasico al Modular (no destructivo): estampa el motor,
    /// los asigna a la categoria base "publico" y les agrega una seccion "mod_publica" mapeando los campos
    /// base (nombre/ide/correo/telefono/ciudad/cargo) sin borrar su FichasJson previo. Idempotente.
    /// Devuelve cuantos migro.</summary>
    Task<int> MigrateAllFromClasicoAsync(CancellationToken cancellationToken = default);
}
