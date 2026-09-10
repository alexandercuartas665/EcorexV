using Ecorex.Domain.Enums;

namespace Ecorex.Application.Directorio;

/// <summary>
/// CRUD de las SECCIONES y CAMPOS del motor Modular (Capa 8), para el modal "Configurar directorio".
/// Opera sobre las definiciones con prefijo "mod_" (que el motor Clasico oculta). Tenant-scoped por el
/// filtro global; TenantId estampado al crear. No toca el motor Clasico.
/// </summary>
public interface IDirectorioModularConfigService
{
    // ---- Secciones ----

    /// <summary>Crea una seccion nueva (clave "mod_..." unica) y devuelve su FichaKey.</summary>
    Task<string> CrearSeccionAsync(CancellationToken cancellationToken = default);

    /// <summary>Actualiza los ajustes de una seccion. Las areas no se cambian si es protegida. Error o null.</summary>
    Task<string?> ActualizarSeccionAsync(Guid id, string title, string? icono, string? color,
        string? descripcion, string? areas, string? aplicaA, CancellationToken cancellationToken = default);

    /// <summary>Elimina una seccion (y sus campos y su uso en categorias). No si es protegida. Error o null.</summary>
    Task<string?> BorrarSeccionAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Mueve la seccion una posicion (delta -1 izquierda / +1 derecha) intercambiando SortOrder.</summary>
    Task<bool> MoverSeccionAsync(Guid id, int delta, CancellationToken cancellationToken = default);

    // ---- Campos ----

    /// <summary>Agrega un campo a la seccion. Devuelve error o null.</summary>
    Task<string?> CrearCampoAsync(string fichaKey, string label, TerceroFieldType tipo, string ancho,
        string? opciones, bool requerido, string? descripcion, string? notasDesarrollador,
        CancellationToken cancellationToken = default);

    /// <summary>Edita un campo (no cambia su FieldKey). Devuelve error o null.</summary>
    Task<string?> ActualizarCampoAsync(Guid id, string label, TerceroFieldType tipo, string ancho,
        string? opciones, bool requerido, string? descripcion, string? notasDesarrollador,
        CancellationToken cancellationToken = default);

    /// <summary>Elimina un campo. No si es de sistema. Devuelve error o null.</summary>
    Task<string?> BorrarCampoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Mueve un campo dentro de su seccion (delta -1 arriba / +1 abajo).</summary>
    Task<bool> MoverCampoAsync(Guid id, int delta, CancellationToken cancellationToken = default);

    /// <summary>Cambia el ancho de un campo (pequena/media/completa).</summary>
    Task<bool> CambiarAnchoAsync(Guid id, string ancho, CancellationToken cancellationToken = default);
}
