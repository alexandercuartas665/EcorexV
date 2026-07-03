namespace Ecorex.Application.Tenancy;

/// <summary>
/// Resultado tipado de los servicios del nucleo de tareas/proyectos (ADR-0013).
/// Evita excepciones crudas hacia la capa de presentacion: los conflictos de concurrencia
/// optimista y las transiciones de estado invalidas llegan como estados del resultado.
/// </summary>
public enum TaskCoreStatus
{
    Ok = 0,
    /// <summary>La entidad no existe (o pertenece a otro tenant: el filtro global la oculta).</summary>
    NotFound,
    /// <summary>Datos invalidos (validacion de negocio).</summary>
    Invalid,
    /// <summary>Conflicto de concurrencia optimista: otro usuario modifico la entidad primero.</summary>
    Conflict,
    /// <summary>Transicion de estado no permitida por TaskItemStateMachine.</summary>
    InvalidTransition,
    /// <summary>El actor no tiene acceso (ACL de proyecto).</summary>
    Forbidden
}

public sealed record TaskCoreResult<T>(TaskCoreStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == TaskCoreStatus.Ok;

    public static TaskCoreResult<T> Ok(T value) => new(TaskCoreStatus.Ok, value, null);
    public static TaskCoreResult<T> NotFound(string? error = null) => new(TaskCoreStatus.NotFound, default, error ?? "No encontrado.");
    public static TaskCoreResult<T> Invalid(string error) => new(TaskCoreStatus.Invalid, default, error);
    public static TaskCoreResult<T> Conflict(string error) => new(TaskCoreStatus.Conflict, default, error);
    public static TaskCoreResult<T> InvalidTransition(string error) => new(TaskCoreStatus.InvalidTransition, default, error);
    public static TaskCoreResult<T> Forbidden(string error) => new(TaskCoreStatus.Forbidden, default, error);
}

/// <summary>Pagina de resultados para listados con paginacion.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
