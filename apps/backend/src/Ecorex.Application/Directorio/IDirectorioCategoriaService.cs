namespace Ecorex.Application.Directorio;

/// <summary>
/// Gestion de las CATEGORIAS del 2do motor de contactos ("Directorio Modular", Capa 8): crear,
/// renombrar, autorizar areas, componer secciones, reordenar y eliminar; ademas la pertenencia
/// multi-membership de un tercero a varias categorias. Todo tenant-scoped por el filtro global.
/// Opera sobre las tablas propias del motor Modular; no toca el motor Clasico. El SEED de las
/// categorias/secciones/campos por defecto (materializar el prototipo) es un paso aparte.
/// </summary>
public interface IDirectorioCategoriaService
{
    /// <summary>Siembra idempotente de las categorias/secciones/campos por defecto del prototipo
    /// (opcion A: secciones con clave prefijada "mod_"). No re-siembra si ya hay categorias.</summary>
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>Categorias del tenant, ordenadas por SortOrder y luego Title. Siembra por defecto si no hay.</summary>
    Task<IReadOnlyList<DirectorioCategoriaDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<DirectorioCategoriaDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Secciones que componen una categoria (por FichaKey), en orden.</summary>
    Task<IReadOnlyList<DirectorioCategoriaSeccionDto>> GetSeccionesAsync(string categoriaKey, CancellationToken cancellationToken = default);

    /// <summary>Crea una categoria nueva (genera CategoriaKey unica desde el titulo). Null si el titulo es invalido.</summary>
    Task<DirectorioCategoriaDto?> CreateAsync(CreateDirectorioCategoriaRequest request, CancellationToken cancellationToken = default);

    /// <summary>Actualiza una categoria (no cambia su CategoriaKey). Devuelve un mensaje de error o null si OK.</summary>
    Task<string?> UpdateAsync(Guid id, UpdateDirectorioCategoriaRequest request, CancellationToken cancellationToken = default);

    /// <summary>Elimina una categoria. No se puede si es protegida/sistema o si es la ultima. Mensaje de error o null.</summary>
    Task<string?> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Reemplaza la composicion de secciones de una categoria por la lista dada (en ese orden).</summary>
    Task<string?> SetSeccionesAsync(string categoriaKey, IReadOnlyList<string> fichaKeysEnOrden, CancellationToken cancellationToken = default);

    /// <summary>Reordena la categoria una posicion hacia arriba (up=true) o abajo.</summary>
    Task<bool> ReorderAsync(Guid id, bool up, CancellationToken cancellationToken = default);

    // ---- Multi-membership tercero <-> categoria ----

    /// <summary>Agrega el tercero a la categoria (idempotente). Mensaje de error o null si OK.</summary>
    Task<string?> AsignarTerceroAsync(Guid terceroId, string categoriaKey, CancellationToken cancellationToken = default);

    /// <summary>Quita el tercero de la categoria. Mensaje de error o null si OK.</summary>
    Task<string?> QuitarTerceroAsync(Guid terceroId, string categoriaKey, CancellationToken cancellationToken = default);

    /// <summary>CategoriaKeys a las que pertenece el tercero.</summary>
    Task<IReadOnlyList<string>> CategoriasDeTerceroAsync(Guid terceroId, CancellationToken cancellationToken = default);
}
