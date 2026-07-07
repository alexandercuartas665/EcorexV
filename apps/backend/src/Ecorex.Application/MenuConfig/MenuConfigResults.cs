namespace Ecorex.Application.MenuConfig;

/// <summary>
/// Resultado tipado de los servicios del menu configurable (mismo patron que InventoryResult):
/// sin excepciones crudas hacia la presentacion.
/// </summary>
public enum MenuConfigStatus
{
    Ok = 0,
    /// <summary>La entidad no existe (o pertenece a otro tenant: el filtro global la oculta).</summary>
    NotFound,
    /// <summary>Datos invalidos (validacion de negocio).</summary>
    Invalid,
    /// <summary>Conflicto (unicidad, ej. nombre de vista ya usado).</summary>
    Conflict
}

public sealed record MenuConfigResult<T>(MenuConfigStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == MenuConfigStatus.Ok;

    public static MenuConfigResult<T> Ok(T value) => new(MenuConfigStatus.Ok, value, null);
    public static MenuConfigResult<T> NotFound(string? error = null) => new(MenuConfigStatus.NotFound, default, error ?? "No encontrado.");
    public static MenuConfigResult<T> Invalid(string error) => new(MenuConfigStatus.Invalid, default, error);
    public static MenuConfigResult<T> Conflict(string error) => new(MenuConfigStatus.Conflict, default, error);
}
