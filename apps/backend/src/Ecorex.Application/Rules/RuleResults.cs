namespace Ecorex.Application.Rules;

/// <summary>
/// Resultado tipado de los servicios del motor de reglas (mismo patron que TaskCoreResults
/// y FormResults, ADR-0013/0015/0016): sin excepciones crudas hacia la presentacion.
/// </summary>
public enum RuleServiceStatus
{
    Ok = 0,
    /// <summary>La entidad no existe (o pertenece a otro tenant: el filtro global la oculta).</summary>
    NotFound,
    /// <summary>Datos invalidos (validacion de negocio) o verbo no registrado.</summary>
    Invalid,
    /// <summary>Conflicto (unicidad / concurrencia).</summary>
    Conflict
}

public sealed record RuleResult<T>(RuleServiceStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == RuleServiceStatus.Ok;

    public static RuleResult<T> Ok(T value) => new(RuleServiceStatus.Ok, value, null);
    public static RuleResult<T> NotFound(string? error = null) => new(RuleServiceStatus.NotFound, default, error ?? "No encontrado.");
    public static RuleResult<T> Invalid(string error) => new(RuleServiceStatus.Invalid, default, error);
    public static RuleResult<T> Conflict(string error) => new(RuleServiceStatus.Conflict, default, error);

    /// <summary>Invalid que ademas transporta el valor (ej. outcome Failed ya registrado en historial).</summary>
    public static RuleResult<T> InvalidWithValue(T value, string error) => new(RuleServiceStatus.Invalid, value, error);
}
