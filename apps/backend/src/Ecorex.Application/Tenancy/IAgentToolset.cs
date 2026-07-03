namespace Ecorex.Application.Tenancy;

/// <summary>
/// Resultado de ejecutar una herramienta de agente: el JSON que se devuelve al modelo y una marca
/// de si la herramienta concreto un CIERRE de la sesion (p.ej. crear_lead), para que el motor
/// limpie la cache de la sesion y quede listo para un nuevo cliente.
/// </summary>
public sealed record AgentToolResult(string Json, bool SessionCompleted);

/// <summary>
/// Un grupo de herramientas (function calling / "MCP") que el agente de IA puede invocar.
/// Cada toolset expone sus definiciones (GetSpecs) y ejecuta por nombre (ExecuteAsync).
/// El motor de inferencia agrega TODOS los toolsets registrados y filtra por las herramientas
/// que el agente tiene habilitadas (ver AiAgent.DisabledToolsJson).
/// </summary>
public interface IAgentToolset
{
    /// <summary>Clave estable del grupo (ej. "pipeline"). No se muestra al usuario.</summary>
    string GroupKey { get; }

    /// <summary>Etiqueta visible del grupo para la UI (ej. "Pipeline comercial").</summary>
    string GroupLabel { get; }

    /// <summary>Definiciones (nombre + JSON Schema) que se envian al proveedor de IA.</summary>
    IReadOnlyList<AiToolSpec> GetSpecs();

    /// <summary>Ejecuta una herramienta por nombre con los argumentos (JSON) que pidio el modelo.</summary>
    Task<AgentToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default);
}
