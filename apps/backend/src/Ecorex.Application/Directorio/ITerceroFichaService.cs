namespace Ecorex.Application.Directorio;

/// <summary>Una ficha (pildora/categoria) configurable del Directorio General.</summary>
public sealed record TerceroFichaDto(
    Guid Id, string FichaKey, string Title, string? Description,
    string? Color, string? Perfil, int SortOrder, bool IsSystem, bool IsHidden);

/// <summary>
/// Gestion de las FICHAS (pildoras) del Directorio General (000232), configurables por tenant:
/// crear, renombrar, recolorear, eliminar y reordenar. Fuente de verdad UNICA (antes hardcodeado
/// en 3 sitios). Todo tenant-scoped por el filtro global.
/// </summary>
public interface ITerceroFichaService
{
    /// <summary>Fichas del tenant, ordenadas por SortOrder. Siembra las 5 por defecto si no hay ninguna.</summary>
    Task<IReadOnlyList<TerceroFichaDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Siembra las 5 fichas por defecto (idempotente) para el tenant del contexto.</summary>
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>Crea una ficha nueva (genera FichaKey unica desde el titulo). Devuelve null si el titulo es invalido.</summary>
    Task<TerceroFichaDto?> CreateAsync(string title, string? color, string? perfil, CancellationToken cancellationToken = default);

    /// <summary>Actualiza titulo, color y perfil de visibilidad de la ficha (no cambia su FichaKey).
    /// Devuelve un mensaje de error o null si OK.</summary>
    Task<string?> UpdateAsync(Guid id, string title, string? color, string? perfil, CancellationToken cancellationToken = default);

    /// <summary>Oculta o muestra la ficha en el modal del tercero (sin eliminarla ni tocar sus campos).</summary>
    Task<string?> SetHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina una ficha. No se puede si es de sistema o si aun tiene campos (hay que moverlos o
    /// borrarlos antes). Devuelve un mensaje de error, o null si se elimino.
    /// </summary>
    Task<string?> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Reordena la ficha una posicion hacia arriba (up=true) o abajo.</summary>
    Task<bool> ReorderAsync(Guid id, bool up, CancellationToken cancellationToken = default);
}
