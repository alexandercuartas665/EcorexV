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

        // ADR-0090 ola C: si el nodo tiene FORMULARIO, el agente lo LLENA con tool-calling (ver/fijar/enviar).
        // Es un bucle acotado que ACUMULA valores en memoria (este servicio no escribe BD); el runner los
        // envia por SaveAsync con la misma validacion que un humano.
        if (context.Node.Form is not null)
        {
            return await RunFormFillAsync(context, agent, providerCfg.BaseUrl, apiKey, model, cancellationToken);
        }

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
        var esCompuerta = context.Node.NodeType == WorkflowNodeType.ExclusiveGateway;
        if (esCompuerta)
        {
            // Compuerta atendida (ADR-0090 ola B): la decision es ELEGIR una de las rutas listadas.
            sb.AppendLine("Este paso es una COMPUERTA: debes ELEGIR por cual ruta continua el proceso.");
            sb.AppendLine("Responde UNICAMENTE con un objeto JSON, sin texto alrededor y sin bloques de codigo:");
            sb.AppendLine("""{"puede_resolver": true|false, "ruta": "<clave de la ruta elegida>", "comentario": "<justificacion breve>"}""");
            sb.AppendLine();
            sb.AppendLine("Reglas:");
            sb.AppendLine("- 'ruta' debe ser EXACTAMENTE la clave (o el nombre) de una de las rutas listadas en 'Rutas de la compuerta'. No inventes rutas.");
            sb.AppendLine("- Si el contexto NO alcanza para elegir con seguridad, responde puede_resolver=false y explica que falta en comentario.");
            sb.AppendLine("- Nunca inventes datos que no esten en el contexto: el caso pasa a una persona si dudas.");
        }
        else
        {
            sb.AppendLine("Responde UNICAMENTE con un objeto JSON, sin texto alrededor y sin bloques de codigo:");
            sb.AppendLine("""{"puede_resolver": true|false, "resultado": "<decision>", "comentario": "<justificacion breve>"}""");
            sb.AppendLine();
            sb.AppendLine("Reglas:");
            sb.AppendLine("- Si el contexto NO alcanza para decidir con seguridad, responde puede_resolver=false y explica que falta en comentario.");
            sb.AppendLine("- Nunca inventes datos que no esten en el contexto: el caso pasa a una persona si dudas.");
            sb.AppendLine("- 'resultado' debe ser una sola palabra corta (por ejemplo Approved o Rejected) coherente con el paso.");
            if (context.Node.Routes is { Count: > 0 })
            {
                // Patron Task->compuerta: el 'resultado' debe cumplir una condicion listada para enrutar bien.
                sb.AppendLine("- Tras este paso hay una compuerta: elige un 'resultado' que cumpla una de las condiciones listadas en 'Compuerta a continuacion'.");
            }
        }
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

    // ---- ADR-0090 ola C: LLENAR el formulario del paso con tool-calling (ver/fijar/enviar) ----

    /// <summary>Tope de rondas del bucle de llenado: ver/fijar/enviar no necesitan muchas vueltas; acota tokens.</summary>
    private const int MaxFormRounds = 8;

    /// <summary>Corre el bucle de function-calling para diligenciar el formulario del paso. ACUMULA los valores
    /// en memoria (este servicio NO escribe BD); el runner los envia por SaveAsync con la validacion real. El
    /// modelo termina llamando 'enviar_formulario'; si no lo hace, es "no pudo" y el paso vuelve a una persona.</summary>
    private async Task<WorkflowAgentInvocationResult> RunFormFillAsync(
        WorkflowAgentContextDto context, Domain.Entities.AiAgent agent, string? baseUrl, string apiKey, string model,
        CancellationToken cancellationToken)
    {
        var form = context.Node.Form!;
        var tools = BuildFormTools();
        var system = BuildFormSystemPrompt(agent.SystemPrompt, context);
        var userPrompt = WorkflowAgentContextSerializer.ToText(context);
        if (userPrompt.Length > MaxPromptChars) { userPrompt = userPrompt[..MaxPromptChars] + "\n[...contexto recortado...]"; }
        var messages = new List<AiToolMessage> { new("user", userPrompt) };

        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? finalComment = null;
        var finished = false;
        int inTokens = 0, outTokens = 0;

        for (var round = 0; round < MaxFormRounds && !finished; round++)
        {
            AiCompletion completion;
            try
            {
                completion = await _client.CompleteWithToolsAsync(
                    agent.Provider, apiKey, baseUrl, model, system, messages, tools, cancellationToken);
            }
            catch (Exception ex)
            {
                return WorkflowAgentInvocationResult.Failed(
                    $"Error llamando al proveedor {agent.Provider}: {ex.Message}", agent.Provider, model, inTokens, outTokens);
            }

            inTokens += completion.InputTokens;
            outTokens += completion.OutputTokens;
            if (!completion.Ok)
            {
                return WorkflowAgentInvocationResult.Failed(
                    completion.Error ?? "El proveedor de IA no respondio.", agent.Provider, model, inTokens, outTokens);
            }

            if (completion.ToolCalls.Count == 0)
            {
                // El modelo termino sin llamar 'enviar_formulario': no completo el llenado.
                finalComment ??= completion.Text;
                break;
            }

            messages.Add(new AiToolMessage("assistant", completion.Text, completion.ToolCalls));
            foreach (var call in completion.ToolCalls)
            {
                var result = call.Name switch
                {
                    "ver_formulario" => FormSchemaJson(form),
                    "fijar_campos" => ApplySetFields(call.ArgumentsJson, form, fields),
                    "enviar_formulario" => MarkFinished(call.ArgumentsJson, ref finished, ref finalComment),
                    _ => $$"""{"error": "herramienta '{{call.Name}}' no disponible"}"""
                };
                messages.Add(new AiToolMessage("tool", result, ToolCallId: call.Id, ToolName: call.Name));
            }
        }

        if (!finished || fields.Count == 0)
        {
            var why = fields.Count == 0
                ? "El agente no fijo ningun valor del formulario."
                : "El agente no llamo 'enviar_formulario' (no termino de llenar el formulario).";
            var reason = string.IsNullOrWhiteSpace(finalComment) ? why : $"{why} {finalComment}";
            return WorkflowAgentInvocationResult.Failed(reason, agent.Provider, model, inTokens, outTokens);
        }

        return new WorkflowAgentInvocationResult(
            true, Result: null, Comment: Clip(finalComment, 2000), Error: null,
            agent.Provider, model, inTokens, outTokens, Route: null, Fields: fields);
    }

    private static IReadOnlyList<AiToolSpec> BuildFormTools() => new[]
    {
        new AiToolSpec("ver_formulario",
            "Devuelve el esquema del formulario del paso: campos con codigo, etiqueta, tipo, si es obligatorio y sus opciones.",
            """{"type":"object","properties":{}}"""),
        new AiToolSpec("fijar_campos",
            "Fija valores del formulario. 'campos' es un objeto {codigo_de_campo: valor}. Puedes llamarla varias veces; se acumulan.",
            """{"type":"object","properties":{"campos":{"type":"object"}},"required":["campos"]}"""),
        new AiToolSpec("enviar_formulario",
            "Marca el formulario como LISTO cuando ya fijaste todos los campos obligatorios. Acepta 'comentario' opcional.",
            """{"type":"object","properties":{"comentario":{"type":"string"}}}"""),
    };

    private static string BuildFormSystemPrompt(string agentPrompt, WorkflowAgentContextDto context)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(agentPrompt)) { sb.AppendLine(agentPrompt.Trim()); sb.AppendLine(); }
        sb.AppendLine("Atiendes un paso de un proceso de negocio que exige DILIGENCIAR un formulario.");
        sb.AppendLine("Herramientas: 'ver_formulario' (esquema), 'fijar_campos' (pon valores con {\"campos\":{codigo:valor}}), 'enviar_formulario' (marca LISTO al terminar).");
        sb.AppendLine("Reglas:");
        sb.AppendLine("- Llena SOLO con datos presentes en el contexto (caso, tercero, datos capturados antes). NUNCA inventes datos.");
        sb.AppendLine("- Respeta los campos OBLIGATORIOS. En listas/opciones usa un valor valido de 'opciones'.");
        sb.AppendLine("- Si NO puedes llenar los obligatorios con lo que hay, NO llames 'enviar_formulario' y explica que falta en 'comentario'.");
        if (context.Assignment?.Autonomy == WorkflowAgentAutonomy.Proposes)
        {
            sb.AppendLine("- Lo que llenes es una PROPUESTA: una persona lo revisara antes de enviarlo.");
        }
        else
        {
            sb.AppendLine("- Al enviar, el formulario se guarda y el proceso avanza sin revision humana. Ante la duda, no envies.");
        }
        return sb.ToString();
    }

    private static string FormSchemaJson(WorkflowAgentFormDto form)
    {
        var payload = new
        {
            titulo = form.Title,
            codigo = form.Code,
            campos = form.Fields.Select(f => new
            {
                codigo = f.FieldCode,
                etiqueta = f.Label,
                tipo = f.ControlType.ToString(),
                obligatorio = f.Required,
                ayuda = f.HelpText,
                opciones = f.OptionsJson
            })
        };
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Aplica 'fijar_campos': acepta SOLO codigos que existen en el formulario (ignora los demas),
    /// convierte el valor a texto y los acumula. Devuelve al modelo que quedo fijado y que obligatorios faltan.</summary>
    private static string ApplySetFields(string argsJson, WorkflowAgentFormDto form, Dictionary<string, string?> fields)
    {
        var known = form.Fields.Select(f => f.FieldCode).ToHashSet(StringComparer.Ordinal);
        var ignored = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            if (!doc.RootElement.TryGetProperty("campos", out var campos) || campos.ValueKind != JsonValueKind.Object)
            {
                return """{"error": "'campos' debe ser un objeto {codigo: valor}"}""";
            }
            foreach (var p in campos.EnumerateObject())
            {
                if (!known.Contains(p.Name)) { ignored.Add(p.Name); continue; }
                fields[p.Name] = ScalarToString(p.Value);
            }
        }
        catch (JsonException)
        {
            return """{"error": "JSON invalido en 'campos'"}""";
        }

        var missing = form.Fields
            .Where(f => f.Required && (!fields.TryGetValue(f.FieldCode, out var v) || string.IsNullOrWhiteSpace(v)))
            .Select(f => f.FieldCode).ToList();
        var payload = new { ok = true, fijados = fields.Keys.ToList(), faltan_obligatorios = missing, ignorados_desconocidos = ignored };
        return JsonSerializer.Serialize(payload);
    }

    private static string MarkFinished(string argsJson, ref bool finished, ref string? comment)
    {
        finished = true;
        var c = ReadCommentArg(argsJson);
        if (!string.IsNullOrWhiteSpace(c)) { comment = c; }
        return """{"ok": true, "mensaje": "formulario marcado como listo"}""";
    }

    private static string? ReadCommentArg(string argsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            return doc.RootElement.TryGetProperty("comentario", out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? ScalarToString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => el.GetRawText()
    };

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) { return null; }
        var t = value.Trim();
        return t.Length <= max ? t : t[..max];
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

    /// <summary>Tope de la clave/ruta elegida (BpmnElementId del destino o su nombre); mas holgado que un
    /// resultado corto pero acotado como red de seguridad.</summary>
    private const int MaxRouteChars = 200;

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
            // ADR-0090 ola B: en una compuerta la decision es 'ruta' (clave del destino). El parser lee AMBOS
            // y no impone cual; el runner exige el que corresponda al tipo de nodo. La ruta NO se recorta a 20:
            // es una clave (BpmnElementId) o el nombre del destino, que puede ser mas largo.
            var route = Clip(ReadString(root, "ruta") ?? ReadString(root, "route"), MaxRouteChars);
            if (string.IsNullOrWhiteSpace(result) && string.IsNullOrWhiteSpace(route))
            {
                return WorkflowAgentInvocationResult.Failed("El agente no indico un resultado ni una ruta para el paso.");
            }
            return new WorkflowAgentInvocationResult(true, result, comment, null, Route: route);
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
