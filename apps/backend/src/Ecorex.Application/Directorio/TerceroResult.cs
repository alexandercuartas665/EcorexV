namespace Ecorex.Application.Directorio;

/// <summary>
/// Resultado tipado de los servicios del Directorio General (mismo patron que RolResult /
/// OrgResult): sin excepciones crudas hacia la presentacion.
/// </summary>
public enum TerceroServiceStatus
{
    Ok = 0,
    /// <summary>La entidad no existe (o pertenece a otro tenant: el filtro global la oculta).</summary>
    NotFound,
    /// <summary>Datos invalidos o regla de negocio violada.</summary>
    Invalid,
    /// <summary>Conflicto (ej. asignar una empresa como si fuera contacto).</summary>
    Conflict
}

public sealed record TerceroResult<T>(TerceroServiceStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == TerceroServiceStatus.Ok;

    public static TerceroResult<T> Ok(T value) => new(TerceroServiceStatus.Ok, value, null);
    public static TerceroResult<T> NotFound(string? error = null) => new(TerceroServiceStatus.NotFound, default, error ?? "No encontrado.");
    public static TerceroResult<T> Invalid(string error) => new(TerceroServiceStatus.Invalid, default, error);
    public static TerceroResult<T> Conflict(string error) => new(TerceroServiceStatus.Conflict, default, error);
}
