using Ecorex.Domain.Enums;

namespace Ecorex.Application.Scraping;

// ==== Configuracion de flujos de extraccion por navegador (modulo 000730, capitulo "Extraccion de
// Datos", Ola 1). Solo CONFIGURACION: se guarda el flujo + pasos + variables; el runtime (el
// sub-agente Navegador) es diferido. Las variables secretas viajan en claro SOLO al guardar (input)
// y se cifran con ISecretProtector; NUNCA se devuelven en claro. ====

/// <summary>Resumen de un flujo para la lista lateral.</summary>
public sealed record ScrapeFlowSummaryDto(
    Guid Id,
    string Name,
    ScrapeSourceStatus Status,
    int StepCount,
    string? ClientName,
    string? ContainerName,
    DateTimeOffset? LastRunAt,
    string? LastResultSummary);

/// <summary>Un paso, con todos los campos (los que no apliquen a su Kind quedan null).</summary>
public sealed record ScrapeStepDto(
    Guid Id,
    Guid FlowId,
    int Order,
    ScrapeStepKind Kind,
    string Name,
    int? WaitMs,
    string? Url,
    string? Script,
    string? Selector,
    string? MappingJson,
    string? Instruction,
    Guid? TargetContainerId,
    string? ToolAllowListJson,
    int? MaxSteps,
    int? MaxSeconds,
    Guid? AiProviderId,
    string? AiModel,
    string? WarningLabel = null,
    ScrapeWarningAction WarningAction = ScrapeWarningAction.None,
    string? OutputVar = null,
    string? LoopOverVar = null,
    bool IsLoopEnd = false,
    string? IngestPath = null,
    string? IngestKeyColumn = null,
    string? PageCountVar = null,
    bool IsPageNext = false,
    Guid? RuleId = null,
    string? RuleInputVar = null);

/// <summary>Una regla del tenant, para el selector del paso ENSAMBLADO (Ola 4).</summary>
public sealed record ScrapeRuleOptionDto(Guid Id, string Label);

// ==== Portabilidad de PASOS (export/import JSON). Mueve SOLO los pasos de un flujo entre entornos
// (p.ej. local -> produccion). Las variables NO viajan: se portan a mano. Al importar se REEMPLAZAN
// todos los pasos del flujo destino. Las referencias por Guid a entidades de otro entorno (contenedor
// destino, proveedor IA, regla) se incluyen pero se VALIDAN contra el tenant destino al importar: si no
// resuelven, el campo queda en null y se avisa para re-enlazar en la UI. ====

/// <summary>Un paso en formato PORTABLE. Excluye Id/FlowId/TenantId y auditoria: al importar nacen
/// pasos nuevos en el flujo destino.</summary>
public sealed record ScrapeStepPortable(
    int Order,
    ScrapeStepKind Kind,
    string Name,
    int? WaitMs,
    string? Url,
    string? Script,
    string? Selector,
    string? MappingJson,
    string? Instruction,
    Guid? TargetContainerId,
    string? ToolAllowListJson,
    int? MaxSteps,
    int? MaxSeconds,
    Guid? AiProviderId,
    string? AiModel,
    string? WarningLabel,
    ScrapeWarningAction WarningAction,
    string? OutputVar,
    string? LoopOverVar,
    bool IsLoopEnd,
    string? IngestPath,
    string? IngestKeyColumn,
    string? PageCountVar,
    bool IsPageNext,
    Guid? RuleId,
    string? RuleInputVar);

/// <summary>El paquete exportable con los PASOS de un flujo (version de esquema + nombre origen +
/// sello + pasos ordenados). Sin variables, sin ids.</summary>
public sealed record ScrapeStepsPackage(
    int SchemaVersion,
    string FlowName,
    DateTimeOffset ExportedAt,
    IReadOnlyList<ScrapeStepPortable> Steps);

/// <summary>Resultado de exportar: nombre de archivo sugerido + bytes UTF-8 del JSON.</summary>
public sealed record ScrapeStepsExportDto(string FileName, byte[] Bytes);

/// <summary>Resultado de importar pasos (reemplazo total). <paramref name="Replaced"/> = pasos que
/// habia antes; <paramref name="Imported"/> = pasos nuevos. <paramref name="Warnings"/> lista las
/// referencias por Guid que no existian en el tenant destino (quedaron en null).</summary>
public sealed record ScrapeStepsImportResultDto(
    bool Ok,
    int Imported,
    int Replaced,
    IReadOnlyList<string> Warnings,
    string? Error);

/// <summary>Una variable. <paramref name="Value"/> trae el valor EN CLARO solo de las NO secretas
/// (se guardan en texto plano); en las secretas va null y se consulta aparte con
/// <see cref="IScrapeFlowService.RevealVariableSecretAsync"/>. El modulo es administrativo (policy
/// ExtraccionDatos.Editar, solo tecnicos), por eso se muestra el valor de las no secretas.</summary>
public sealed record ScrapeVariableDto(
    Guid Id,
    Guid FlowId,
    string Name,
    bool HasValue,
    bool IsSecret,
    string? Value);

/// <summary>Resultado de pedir el valor en claro de una variable secreta (bajo demanda). Si
/// <paramref name="Ok"/> es false, <paramref name="Error"/> explica por que (p.ej. cifrada con la
/// llave de otro entorno -no desencripta-).</summary>
public sealed record ScrapeSecretRevealDto(
    bool Ok,
    string? Value,
    string? Error);

/// <summary>Una tabla que un flujo puede usar como destino ("Modelo / Tabla"), para el selector.</summary>
public sealed record ScrapeTargetDto(Guid Id, string Label);

/// <summary>La programacion de un flujo (Ola 5): reusa ImportProcess/recurrencia del Contenedor.</summary>
public sealed record ScrapeScheduleDto(
    ImportScheduleKind Kind,
    int? IntervalMinutes,
    string? CronExpression,
    bool IsActive,
    DateTimeOffset? NextRunAt,
    string? DisabledReason);

/// <summary>Alta/edicion de la programacion de un flujo.</summary>
public sealed record SaveScrapeScheduleRequest(
    Guid FlowId,
    ImportScheduleKind Kind,
    int? IntervalMinutes,
    string? CronExpression,
    bool IsActive);

/// <summary>Una corrida en la bitacora del flujo (runtime, Ola 3).</summary>
public sealed record ScrapeFlowRunDto(
    Guid Id,
    DateTimeOffset FiredAt,
    DateTimeOffset? FinishedAt,
    ImportRunTrigger Trigger,
    ImportRunResult Result,
    int StepCount,
    int Inserted,
    string? Detail);

/// <summary>El flujo completo (cabecera + pasos ordenados + variables).</summary>
public sealed record ScrapeFlowDto(
    Guid Id,
    string Name,
    string? Description,
    string StartUrl,
    ScrapeSourceStatus Status,
    Guid? ClientId,
    string? ClientName,
    Guid? ContainerId,
    string? ContainerName,
    DateTimeOffset? LastRunAt,
    string? LastResultSummary,
    IReadOnlyList<ScrapeStepDto> Steps,
    IReadOnlyList<ScrapeVariableDto> Variables,
    string? PageVar = null,
    int? PageFrom = null,
    int? PageTo = null);

/// <summary>Alta/edicion de la CABECERA de un flujo (los pasos y variables se guardan aparte).</summary>
public sealed record SaveScrapeFlowRequest(
    Guid? Id,
    string Name,
    string? Description,
    string StartUrl,
    ScrapeSourceStatus Status,
    Guid? ClientId,
    Guid? ContainerId,
    string? PageVar = null,
    int? PageFrom = null,
    int? PageTo = null);

public sealed record SaveScrapeStepRequest(
    Guid? Id,
    Guid FlowId,
    int Order,
    ScrapeStepKind Kind,
    string Name,
    int? WaitMs = null,
    string? Url = null,
    string? Script = null,
    string? Selector = null,
    string? MappingJson = null,
    string? Instruction = null,
    Guid? TargetContainerId = null,
    string? ToolAllowListJson = null,
    int? MaxSteps = null,
    int? MaxSeconds = null,
    Guid? AiProviderId = null,
    string? AiModel = null,
    string? WarningLabel = null,
    ScrapeWarningAction WarningAction = ScrapeWarningAction.None,
    string? OutputVar = null,
    string? LoopOverVar = null,
    bool IsLoopEnd = false,
    string? IngestPath = null,
    string? IngestKeyColumn = null,
    string? PageCountVar = null,
    bool IsPageNext = false,
    Guid? RuleId = null,
    string? RuleInputVar = null);

/// <summary>Alta/edicion de una variable. <paramref name="Value"/> en claro (input); se cifra al
/// persistir si <paramref name="IsSecret"/>. Si es edicion y Value llega null, se conserva el valor
/// existente (no se puede consultar un secreto, pero si reescribirlo).</summary>
public sealed record SaveScrapeVariableRequest(
    Guid? Id,
    Guid FlowId,
    string Name,
    string? Value,
    bool IsSecret);

/// <summary>
/// CRUD de la configuracion de flujos de extraccion. Tenant-scoped por el filtro global. Solo
/// configuracion (sin runtime). Las variables secretas se cifran con ISecretProtector.
/// </summary>
public interface IScrapeFlowService
{
    Task<IReadOnlyList<ScrapeFlowSummaryDto>> ListAsync(CancellationToken ct = default);
    /// <summary>Tablas que un flujo puede usar como destino, etiquetadas "Modelo / Tabla".</summary>
    Task<IReadOnlyList<ScrapeTargetDto>> ListContainersAsync(CancellationToken ct = default);
    /// <summary>Reglas del tenant para el paso ENSAMBLADO (Ola 4), etiquetadas "Documento / Regla (verbo)".</summary>
    Task<IReadOnlyList<ScrapeRuleOptionDto>> ListRulesAsync(CancellationToken ct = default);
    Task<ScrapeFlowDto?> GetAsync(Guid flowId, CancellationToken ct = default);
    /// <summary>Bitacora de corridas del flujo, mas recientes primero (runtime, Ola 3).</summary>
    Task<IReadOnlyList<ScrapeFlowRunDto>> ListRunsAsync(Guid flowId, int take = 10, CancellationToken ct = default);
    /// <summary>Programacion del flujo (Ola 5), o null si no tiene una todavia.</summary>
    Task<ScrapeScheduleDto?> GetScheduleAsync(Guid flowId, CancellationToken ct = default);
    /// <summary>Crea o actualiza la programacion del flujo (reusa ImportProcess). Rechaza cron invalido.</summary>
    Task<ScrapeScheduleDto> SaveScheduleAsync(SaveScrapeScheduleRequest req, Guid actorUserId, CancellationToken ct = default);
    Task<ScrapeFlowDto?> SaveFlowAsync(SaveScrapeFlowRequest req, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteFlowAsync(Guid flowId, Guid actorUserId, CancellationToken ct = default);

    Task<ScrapeStepDto?> SaveStepAsync(SaveScrapeStepRequest req, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteStepAsync(Guid stepId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Exporta SOLO los pasos de un flujo a un paquete JSON portable (sin variables, sin ids).
    /// Null si el flujo no existe en el tenant.</summary>
    Task<ScrapeStepsExportDto?> ExportStepsAsync(Guid flowId, CancellationToken ct = default);
    /// <summary>Importa pasos desde un paquete JSON, REEMPLAZANDO todos los pasos del flujo destino en
    /// una sola transaccion. Las referencias por Guid que no existan en el tenant destino se dejan en
    /// null y se reportan en Warnings. No toca variables. Si el JSON es invalido, Ok=false con Error.</summary>
    Task<ScrapeStepsImportResultDto> ImportStepsAsync(Guid flowId, string json, Guid actorUserId, CancellationToken ct = default);
    /// <summary>Reordena los pasos de un flujo segun el orden de la lista de ids.</summary>
    Task<bool> ReorderStepsAsync(Guid flowId, IReadOnlyList<Guid> orderedStepIds, Guid actorUserId, CancellationToken ct = default);

    Task<ScrapeVariableDto?> SaveVariableAsync(SaveScrapeVariableRequest req, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteVariableAsync(Guid variableId, Guid actorUserId, CancellationToken ct = default);
    /// <summary>Devuelve el valor EN CLARO de una variable (bajo demanda, para el modulo tecnico).
    /// Descifra las secretas; si la secreta se cifro con la llave de otro entorno no desencripta y
    /// Ok=false con el motivo. Null si la variable no existe en el tenant.</summary>
    Task<ScrapeSecretRevealDto?> RevealVariableSecretAsync(Guid variableId, CancellationToken ct = default);
}
