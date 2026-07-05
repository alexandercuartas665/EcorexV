namespace Ecorex.Application.Inventory;

/// <summary>
/// Resultado tipado de los servicios de inventario (mismo patron que OrgResults / RuleResults,
/// ADR-0013/0016/0017): sin excepciones crudas hacia la presentacion.
/// </summary>
public enum InventoryServiceStatus
{
    Ok = 0,
    /// <summary>La entidad no existe (o pertenece a otro tenant: el filtro global la oculta).</summary>
    NotFound,
    /// <summary>Datos invalidos (validacion de negocio).</summary>
    Invalid,
    /// <summary>Conflicto (unicidad, ej. nombre o SKU ya usado).</summary>
    Conflict
}

public sealed record InventoryResult<T>(InventoryServiceStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == InventoryServiceStatus.Ok;

    public static InventoryResult<T> Ok(T value) => new(InventoryServiceStatus.Ok, value, null);
    public static InventoryResult<T> NotFound(string? error = null) => new(InventoryServiceStatus.NotFound, default, error ?? "No encontrado.");
    public static InventoryResult<T> Invalid(string error) => new(InventoryServiceStatus.Invalid, default, error);
    public static InventoryResult<T> Conflict(string error) => new(InventoryServiceStatus.Conflict, default, error);
}
