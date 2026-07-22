using System.Text;
using System.Text.Json;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Implementacion real de <see cref="IWorkflowAgentInvoker"/>: serializa el contexto del paso a un
/// prompt, resuelve la cuenta del proveedor (config global del Super Admin) y llama al modelo.
///
/// NO usa IAiInferenceService a proposito: ese motor esta hecho para CONVERSAR (bucle de
/// herramientas, cache de sesion por contacto, extraccion de datos en una segunda llamada) y
/// registra su consumo con source "test". Atender un paso de flujo es una sola pregunta cerrada de
/// una sola vuelta; meterla por el motor conversacional pagaria llamadas extra, ensuciaria la cache
/// de sesiones con ids de paso y duplicaria el registro de consumo que lleva el runner.
///
/// Este servicio NO escribe en base de datos: solo lee configuracion. Quien decide y persiste es
/// <see cref="WorkflowAgentStepRunner"/>, ya fuera de toda llamada de red.
/// </summary>
public sealed class WorkflowAgentInvoker : IWorkflowAgentInvoker
{
    /// <summary>Tope del texto del contexto enviado al modelo (red de seguridad sobre los topes de la ola 1).</summary>
    private const int MaxPromptChars = 60_000;

    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IAiProviderClient _client;

    public WorkflowAgentInvoker(IApplicationDbContext db, ISecretProtector secretProtector, IAiProviderClient client)
    {
        _db = db;
        _secretProtector = secretProtector;
        _client = client;
    }

    public async Task<WorkflowAgentInvocationResult> InvokeAsync(
        WorkflowAgentContextDto context, CancellationToken cancellationToken = default)
    {
        if (context.Assignment is not { } assignment)
        {
            return WorkflowAgentInvocationResult.Failed("El nodo no tiene un agente de IA asignado.");
        }
        if (!assignment.IsActive)
        {
            return WorkflowAgentInvocationResult.Failed(
                $"El agente '{assignment.AgentName}' esta desactivado: el paso lo debe atender una persona.");
        }

        // AiAgents esta bajo el filtro global de tenant: un agente de otro tenant no existe aqui.
        var agent = await _db.AiAgents.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assignment.AiAgentId, cancellationToken);
        if (agent is null)
        {
            return WorkflowAgentInvocationResult.Failed("El agente asignado al nodo ya no existe.");
        }

        var providerCfg = await _db.AiProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Provider == agent.Provider, cancellationToken);
        if (providerCfg is null || !providerCfg.IsEnabled || string.IsNullOrWhiteSpace(providerCfg.ApiKeyEncrypted))
        {
            return WorkflowAgentInvocationResult.Failed(
                $"El proveedor {agent.Provider} no esta habilitado en la plataforma.", agent.Provider);
        }

        string apiKey;
        try
        {
            apiKey = _secretProtector.Unprotect(providerCfg.ApiKeyEncrypted);
        }
        catch
        {
            return WorkflowAgentInvocationResult.Failed(
                $"La API key del proveedor {agent.Provider} no se pudo descifrar.", agent.Provider);
        }

        var meta = AiProviderCatalog.For(agent.Provider);
        var model = !string.IsNullOrWhiteSpace(agent.Model) ? agent.Model!
            : !string.IsNullOrWhiteSpace(providerCfg.Model) ? providerCfg.Model!
            : meta.DefaultModel;

        var systemPrompt = BuildSystemPrompt(agent.SystemPrompt, context);
        var userPrompt = WorkflowAgentContextSerializer.ToText(context);
        if (userPrompt.Length > MaxPromptChars)
        {
            userPrompt = userPrompt[..MaxPromptChars] + "\n[...contexto recortado...]";
        }

        AiChatResult response;
        try
        {
            response = await _client.CompleteAsync(
                agent.Provider, apiKey, providerCfg.BaseUrl, model, systemPrompt,
                [new AiChatTurn("user", userPrompt)], cancellationToken);
        }
        catch (Exception ex)
        {
            // Un fallo del proveedor NO es una excepcion para el flujo: es "el agente no pudo".
            // Se devuelve como resultado para que el runner devuelva el paso a una persona.
            return WorkflowAgentInvocationResult.Failed(
                $"Error llamando al proveedor {agent.Provider}: {ex.Message}", agent.Provider, model);
        }

        if (!response.Ok || string.IsNullOrWhiteSpace(response.Text))
        {
            return WorkflowAgentInvocationResult.Failed(
                response.Error ?? "El proveedor de IA no devolvio respuesta.",
                agent.Provider, model, response.InputTokens, response.OutputTokens);
        }

        var parsed = WorkflowAgentDecisionParser.Parse(response.Text!);
        return parsed with
        {
            Provider = agent.Provider,
            Model = model,
            InputTokens = response.InputTokens,
            OutputTokens = response.OutputTokens
        };
    }

    /// <summary>
    /// Prompt de sistema: el del agente + el contrato de salida. Se exige JSON estricto porque la
    /// respuesta la consume una maquina que va a CERRAR un paso de proceso; texto libre invitaria a
    /// adivinar, y adivinar en una aprobacion de compra es inaceptable.
    /// </summary>
    private static string BuildSystemPrompt(string agentPrompt, WorkflowAgentContextDto context)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(agentPrompt))
        {
            sb.AppendLine(agentPrompt.Trim());
            sb.AppendLine();
        }
        sb.AppendLine("Atiendes un paso de un proceso de negocio. Vas a recibir el contexto completo del caso.");
        sb.AppendLine("Responde UNICAMENTE con un objeto JSON, sin texto alrededor y sin bloques de codigo:");
        sb.AppendLine("""{"puede_resolver": true|false, "resultado": "<decision>", "comentario": "<justificacion breve>"}""");
        sb.AppendLine();
        sb.AppendLine("Reglas:");
        sb.AppendLine("- Si el contexto NO alcanza para decidir con seguridad, responde puede_resolver=false y explica que falta en comentario.");
        sb.AppendLine("- Nunca inventes datos que no esten en el contexto: el caso pasa a una persona si dudas.");
        sb.AppendLine("- 'resultado' debe ser una sola palabra corta (por ejemplo Approved o Rejected) coherente con el paso.");
        if (context.Assignment?.Autonomy == WorkflowAgentAutonomy.Proposes)
        {
            sb.AppendLine("- Tu respuesta es una PROPUESTA: una persona la revisara antes de que el proceso avance.");
        }
        else
        {
            sb.AppendLine("- Tu respuesta CIERRA el paso y el proceso avanza sin revision humana. Ante la duda, puede_resolver=false.");
        }
        return sb.ToString();
    }
}

/// <summary>
/// Lee la respuesta del modelo. Tolera los envoltorios habituales (bloques ```json, texto antes o
/// despues) pero NO adivina: si no hay un JSON con decision utilizable, es "no pudo resolver" y el
/// paso vuelve a una persona. Preferimos molestar a alguien antes que cerrar un paso en falso.
/// </summary>
public static class WorkflowAgentDecisionParser
{
    /// <summary>Tope del comentario: la columna AgentProposalComment admite 2000 caracteres.</summary>
    private const int MaxCommentChars = 2000;

    /// <summary>Tope del resultado: la columna AgentProposalResult admite 20 caracteres.</summary>
    private const int MaxResultChars = 20;

    public static WorkflowAgentInvocationResult Parse(string text)
    {
        var json = ExtractJson(text);
        if (json is null)
        {
            return WorkflowAgentInvocationResult.Failed(
                "El agente no respondio en el formato esperado (JSON con la decision).");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return WorkflowAgentInvocationResult.Failed("El agente no respondio un objeto JSON con la decision.");
            }

            var comment = Clip(ReadString(root, "comentario") ?? ReadString(root, "comment"), MaxCommentChars);
            var canResolve = ReadBool(root, "puede_resolver") ?? ReadBool(root, "can_resolve") ?? true;
            if (!canResolve)
            {
                return WorkflowAgentInvocationResult.Failed(
                    string.IsNullOrWhiteSpace(comment)
                        ? "El agente indico que no puede resolver el paso con la informacion disponible."
                        : $"El agente no pudo resolver: {comment}");
            }

            var result = Clip(ReadString(root, "resultado") ?? ReadString(root, "result"), MaxResultChars);
            if (string.IsNullOrWhiteSpace(result))
            {
                return WorkflowAgentInvocationResult.Failed("El agente no indico un resultado para el paso.");
            }
            return new WorkflowAgentInvocationResult(true, result, comment, null);
        }
        catch (JsonException)
        {
            return WorkflowAgentInvocationResult.Failed("La respuesta del agente no es un JSON valido.");
        }
    }

    /// <summary>Primer objeto JSON del texto (el modelo a veces lo envuelve en prosa o en ```json).</summary>
    private static string? ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static bool? ReadBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? el.GetBoolean()
            : null;

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) { return null; }
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
