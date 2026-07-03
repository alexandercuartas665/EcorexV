namespace Ecorex.Application.Workflows;

/// <summary>
/// Resultado tipado del WorkflowEngine (mismo patron que TaskCoreResults, ADR-0013):
/// nada de excepciones crudas hacia la capa de presentacion. StuckDetected es el estado
/// heredado del motor legacy: el avance supero el tope de 50 iteraciones y la instancia
/// quedo marcada Stuck (el Value acompana al resultado para diagnostico).
/// </summary>
public enum WorkflowEngineStatus
{
    Ok = 0,
    /// <summary>La entidad no existe (o pertenece a otro tenant: el filtro global la oculta).</summary>
    NotFound,
    /// <summary>Datos invalidos (XML BPMN malformado, paso no vigente, etc.).</summary>
    Invalid,
    /// <summary>Conflicto de concurrencia optimista sobre la instancia.</summary>
    Conflict,
    /// <summary>El avance alcanzo el tope de 50 iteraciones: la instancia quedo Stuck.</summary>
    StuckDetected
}

public sealed record WorkflowResult<T>(WorkflowEngineStatus Status, T? Value, string? Error)
{
    public bool IsOk => Status == WorkflowEngineStatus.Ok;

    public static WorkflowResult<T> Ok(T value) => new(WorkflowEngineStatus.Ok, value, null);
    public static WorkflowResult<T> NotFound(string? error = null) => new(WorkflowEngineStatus.NotFound, default, error ?? "No encontrado.");
    public static WorkflowResult<T> Invalid(string error) => new(WorkflowEngineStatus.Invalid, default, error);
    public static WorkflowResult<T> Conflict(string error) => new(WorkflowEngineStatus.Conflict, default, error);
    /// <summary>Stuck lleva Value: el llamador necesita ver la instancia atascada.</summary>
    public static WorkflowResult<T> Stuck(T value, string error) => new(WorkflowEngineStatus.StuckDetected, value, error);
}
