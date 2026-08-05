namespace Ecorex.Application.Asesores;

/// <summary>Estado tipado del servicio de asesores (mismo patron que TerceroResult).</summary>
public enum AsesorStatus
{
    Ok = 0,
    NotFound,
    Invalid,
    /// <summary>Regla de negocio: no se puede eliminar un asesor con terceros vinculados.</summary>
    Conflict
}

public sealed record AsesorResult<T>(AsesorStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == AsesorStatus.Ok;

    public static AsesorResult<T> Ok(T value) => new(AsesorStatus.Ok, value, null);
    public static AsesorResult<T> NotFound(string? error = null) => new(AsesorStatus.NotFound, default, error ?? "El asesor no existe.");
    public static AsesorResult<T> Invalid(string error) => new(AsesorStatus.Invalid, default, error);
    public static AsesorResult<T> Conflict(string error) => new(AsesorStatus.Conflict, default, error);
}

/// <summary>Fila del catalogo de asesores (000074), con el usuario vinculado (si alguno) y cuantos
/// terceros lo tienen como vendedor asignado (para la UI y para explicar por que no se puede borrar).</summary>
public sealed record AsesorDto(
    Guid Id,
    string Nombre,
    string? Documento,
    string? Email,
    string? Telefono,
    Guid? TenantUserId,
    string? TenantUserNombre,
    bool IsActive,
    int TercerosCount,
    bool AssignableByAgent = false);

/// <summary>Opcion minima para el selector "Vendedor asignado" del tercero.</summary>
public sealed record AsesorOptionDto(Guid Id, string Nombre);

/// <summary>Usuario del tenant vinculable a un asesor (para el selector opcional "es un usuario").</summary>
public sealed record AsesorUserOptionDto(Guid TenantUserId, string Nombre, string Email);

/// <summary>Alta/edicion de un asesor del catalogo.</summary>
public sealed record SaveAsesorRequest(
    string Nombre,
    string? Documento,
    string? Email,
    string? Telefono,
    Guid? TenantUserId,
    bool IsActive,
    bool AssignableByAgent = false);

public interface IAsesorService
{
    Task<IReadOnlyList<AsesorDto>> ListAsync(bool includeInactive = true, CancellationToken cancellationToken = default);

    /// <summary>Asesores activos, minimo, para el selector de "Vendedor asignado" del tercero.</summary>
    Task<IReadOnlyList<AsesorOptionDto>> ListOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Usuarios del tenant que se pueden vincular a un asesor (opcional).</summary>
    Task<IReadOnlyList<AsesorUserOptionDto>> ListLinkableUsersAsync(CancellationToken cancellationToken = default);

    Task<AsesorDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AsesorResult<AsesorDto>> CreateAsync(SaveAsesorRequest request, CancellationToken cancellationToken = default);
    Task<AsesorResult<AsesorDto>> UpdateAsync(Guid id, SaveAsesorRequest request, CancellationToken cancellationToken = default);

    /// <summary>Elimina un asesor. Se BLOQUEA (Conflict) si tiene terceros vinculados como vendedor.</summary>
    Task<AsesorResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
