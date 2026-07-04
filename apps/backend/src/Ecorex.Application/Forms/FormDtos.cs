using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms;

// ---- Formularios dinamicos (FASE 4, ADR-0015) ----

/// <summary>Opcion de un control Select/MultiCheck/Radio ([{id,label,value}] en OptionsJson).</summary>
public sealed record FormOption(string Id, string Label, string? Value = null);

/// <summary>Reglas de validacion declaradas en ValidationJson de la pregunta.</summary>
public sealed record FormValidationRules(
    int? MinLength = null, int? MaxLength = null, string? Pattern = null,
    decimal? MinValue = null, decimal? MaxValue = null);

/// <summary>Valor de un campo en el documento de respuesta: { fieldCode: { value, type } }.</summary>
public sealed record FormFieldValue(string? Value, string Type);

/// <summary>ResponseCount y RuleCount alimentan los KPIs del indice (ADR-0021).</summary>
public sealed record FormDefinitionListItemDto(
    Guid Id, string Code, string Title, string? Description, FormStatus Status,
    int Revision, bool IsArchived, int QuestionCount, long Version,
    int ResponseCount = 0, int RuleCount = 0);

public sealed record FormContainerDto(
    Guid Id, string Name, FormContainerType ContainerType, Guid? ParentId,
    int SortOrder, string? Style,
    string? TabsJson = null, int Width = 12, bool IsLocked = false, bool IsHidden = false);

public sealed record FormQuestionDto(
    Guid Id, Guid? ContainerId, string FieldCode, string Label, string? Caption,
    string? HelpText, FormControlType ControlType, string? OptionsJson, bool Required,
    int SortOrder, string GridCol, string? Numeral, string? ValidationJson,
    int Width = 12, string? PlaceholderText = null, string? DefaultValue = null,
    bool IsLocked = false, bool IsHidden = false);

public sealed record FormDefinitionDetailDto(
    Guid Id, string Code, string Title, string? Description, FormStatus Status,
    int Revision, bool IsArchived, long Version,
    IReadOnlyList<FormContainerDto> Containers,
    IReadOnlyList<FormQuestionDto> Questions);

public sealed record CreateFormDefinitionRequest(string Code, string Title, string? Description = null);

/// <summary>Version es el token de concurrencia optimista leido por el cliente (ADR-0013).</summary>
public sealed record UpdateFormDefinitionRequest(string Title, string? Description, long Version);

public sealed record SaveFormContainerRequest(
    string Name, FormContainerType ContainerType = FormContainerType.Segment,
    Guid? ParentId = null, string? Style = null,
    string? TabsJson = null, int Width = 12, bool IsLocked = false, bool IsHidden = false);

/// <summary>
/// Width (1..12) es la fuente del layout del constructor (ADR-0021). Si viene en 12 (el
/// default) y GridCol trae una columna bootstrap parseable, Width se deriva de GridCol
/// (compatibilidad con callers previos); en cualquier otro caso GridCol se sincroniza
/// desde Width (col-12 / col-md-N).
/// </summary>
public sealed record SaveFormQuestionRequest(
    Guid? ContainerId, string FieldCode, string Label, FormControlType ControlType,
    string? Caption = null, string? HelpText = null, string? OptionsJson = null,
    bool Required = false, string GridCol = "col-12", string? Numeral = null,
    string? ValidationJson = null,
    int Width = 12, string? PlaceholderText = null, string? DefaultValue = null,
    bool IsLocked = false, bool IsHidden = false);

public sealed record FormResponseDto(
    Guid Id, Guid DefinitionId, string? Reference, FormResponseStatus Status,
    IReadOnlyDictionary<string, FormFieldValue> Data,
    DateTimeOffset? SubmittedAt, Guid? SubmittedByTenantUserId, long Version);

/// <summary>Opciones de emision de un token de publicacion por URL.</summary>
public sealed record EmitFormTokenRequest(
    string? Reference = null, int ExpirationHours = 24,
    bool SingleUse = false, bool AllowAnonymous = true);

/// <summary>El Token viaja EN CLARO una unica vez (solo se persiste el hash SHA-256).</summary>
public sealed record EmitFormTokenResult(Guid TokenId, string Token, DateTimeOffset ExpiresAt);

public sealed record FormTokenDto(
    Guid Id, Guid DefinitionId, string? Reference, DateTimeOffset ExpiresAt,
    bool SingleUse, DateTimeOffset? UsedAt, DateTimeOffset? RevokedAt,
    bool AllowAnonymous, DateTimeOffset CreatedAt);

/// <summary>
/// Resultado de validar un token del visor publico. Cuando IsValid es false NO se expone el
/// motivo (expirado/usado/revocado/inexistente) para no filtrar informacion: el visor muestra
/// siempre el mismo mensaje neutro.
/// </summary>
public sealed record FormTokenValidation(
    bool IsValid, Guid? TokenId = null, Guid? TenantId = null, Guid? DefinitionId = null,
    string? Reference = null, bool SingleUse = false, bool AllowAnonymous = false)
{
    public static readonly FormTokenValidation Invalid = new(false);
}

/// <summary>Formulario exigido por un paso current del flujo de una tarea (ADR-0015).</summary>
public sealed record TaskStepFormDto(
    Guid ResponseId, Guid DefinitionId, string FormCode, string FormTitle,
    Guid WorkflowInstanceId, Guid WorkflowNodeId, string? NodeName,
    FormFlowLinkStatus LinkStatus, FormResponseStatus ResponseStatus, string? Reference);
