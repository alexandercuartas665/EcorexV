using System.Text.Json;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Una CORRIDA del agente sobre un paso, en lenguaje legible para una persona: cuando ocurrio, que
/// intento fue, cuantos tokens gasto, como termino y las fases de razonamiento que reporto. Se guarda
/// como JSON en <c>WorkflowStepHistory.AgentRunLog</c> (una lista, se anexa una entrada por corrida o
/// reanudacion). Compartida por el runner (que la escribe) y la UI (que la muestra).
/// </summary>
/// <param name="At">Momento en que termino la corrida (UTC).</param>
/// <param name="Attempt">Numero de intento del agente sobre el paso (1 = primero).</param>
/// <param name="Tokens">Tokens de IA consumidos en esta corrida.</param>
/// <param name="Outcome">Como termino: ok / propuso / en espera / no pudo / cancelado / tiempo agotado.</param>
/// <param name="Rounds">Fases de razonamiento reportadas en vivo durante la corrida (puede ir vacio).</param>
public sealed record WorkflowAgentRunLogEntry(
    DateTimeOffset At,
    int Attempt,
    long Tokens,
    string Outcome,
    IReadOnlyList<string> Rounds);

/// <summary>(De)serializacion tolerante del log del agente. Nunca lanza: un JSON dañado se lee como vacio.</summary>
public static class WorkflowAgentRunLog
{
    /// <summary>Tope de corridas guardadas por paso: se conservan las mas recientes (evita crecer sin fin).</summary>
    private const int MaxEntries = 30;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<WorkflowAgentRunLogEntry> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<WorkflowAgentRunLogEntry>(); }
        try
        {
            return JsonSerializer.Deserialize<List<WorkflowAgentRunLogEntry>>(json, Options)
                ?? (IReadOnlyList<WorkflowAgentRunLogEntry>)Array.Empty<WorkflowAgentRunLogEntry>();
        }
        catch (JsonException)
        {
            return Array.Empty<WorkflowAgentRunLogEntry>();
        }
    }

    /// <summary>Anexa una entrada al log serializado y devuelve el nuevo JSON (recortado a las ultimas MaxEntries).</summary>
    public static string Append(string? existingJson, WorkflowAgentRunLogEntry entry)
    {
        var list = new List<WorkflowAgentRunLogEntry>(Parse(existingJson)) { entry };
        if (list.Count > MaxEntries)
        {
            list = list.Skip(list.Count - MaxEntries).ToList();
        }
        return JsonSerializer.Serialize(list, Options);
    }
}
