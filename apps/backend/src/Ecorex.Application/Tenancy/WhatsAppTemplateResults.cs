namespace Ecorex.Application.Tenancy;

/// <summary>
/// Resultado tipado de los servicios de plantillas HSM de WhatsApp (mismo patron que
/// InventoryResult / OrgResult, ADR-0027/0017): sin excepciones crudas hacia la presentacion.
/// </summary>
public enum WhatsAppTemplateServiceStatus
{
    Ok = 0,
    /// <summary>La entidad no existe (o pertenece a otro tenant: el filtro global la oculta).</summary>
    NotFound,
    /// <summary>Datos invalidos (validacion de negocio).</summary>
    Invalid,
    /// <summary>Conflicto (unicidad, ej. (Name, Language) ya usado).</summary>
    Conflict,
    /// <summary>Operacion no implementada en este corte (deuda documentada, ADR-0029).</summary>
    NotImplemented
}

public sealed record WhatsAppTemplateResult<T>(WhatsAppTemplateServiceStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == WhatsAppTemplateServiceStatus.Ok;

    public static WhatsAppTemplateResult<T> Ok(T value) => new(WhatsAppTemplateServiceStatus.Ok, value, null);
    public static WhatsAppTemplateResult<T> NotFound(string? error = null) => new(WhatsAppTemplateServiceStatus.NotFound, default, error ?? "No encontrado.");
    public static WhatsAppTemplateResult<T> Invalid(string error) => new(WhatsAppTemplateServiceStatus.Invalid, default, error);
    public static WhatsAppTemplateResult<T> Conflict(string error) => new(WhatsAppTemplateServiceStatus.Conflict, default, error);
    public static WhatsAppTemplateResult<T> NotImplemented(string error) => new(WhatsAppTemplateServiceStatus.NotImplemented, default, error);
}
