namespace Ecorex.Application.Roles;

/// <summary>
/// Resultado tipado de los servicios de roles (mismo patron que InventoryResults / OrgResults,
/// ADR-0013/0027/0032): sin excepciones crudas hacia la presentacion.
/// </summary>
public enum RolServiceStatus
{
    Ok = 0,
    /// <summary>La entidad no existe (o pertenece a otro tenant: el filtro global la oculta).</summary>
    NotFound,
    /// <summary>Datos invalidos o regla de negocio violada (ej. borrar rol de sistema o con usuarios).</summary>
    Invalid,
    /// <summary>Conflicto de unicidad (nombre de rol ya usado en el tenant).</summary>
    Conflict
}

public sealed record RolResult<T>(RolServiceStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == RolServiceStatus.Ok;

    public static RolResult<T> Ok(T value) => new(RolServiceStatus.Ok, value, null);
    public static RolResult<T> NotFound(string? error = null) => new(RolServiceStatus.NotFound, default, error ?? "No encontrado.");
    public static RolResult<T> Invalid(string error) => new(RolServiceStatus.Invalid, default, error);
    public static RolResult<T> Conflict(string error) => new(RolServiceStatus.Conflict, default, error);
}
