using System.Text;
using System.Text.Json;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Forms.Calc;
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
    private readonly IAgentBrowserFetch _browserFetch;
    private readonly IEmailSender _email;
    private readonly Forms.IQuoteDocumentRenderer _quoteDoc;
    private readonly Notifications.INotificationChannelSender _sender;

    /// <summary>ADR-0093: tope de correos por paso, red de seguridad anti-bucle (el bucle ya tiene su tope de rondas).</summary>
    private const int MaxEmailsPerStep = 3;

    /// <summary>Tope de impresiones (documentos) que el agente puede ENVIAR por paso (anti-bucle / anti-spam).</summary>
    private const int MaxDocsPerStep = 3;

    public WorkflowAgentInvoker(
        IApplicationDbContext db, ISecretProtector secretProtector, IAiProviderClient client,
        IAgentBrowserFetch browserFetch, IEmailSender email,
        Forms.IQuoteDocumentRenderer quoteDoc, Notifications.INotificationChannelSender sender)
    {
        _db = db;
        _secretProtector = secretProtector;
        _client = client;
        _browserFetch = browserFetch;
        _email = email;
        _quoteDoc = quoteDoc;
        _sender = sender;
    }

    public async Task<WorkflowAgentInvocationResult> InvokeAsync(
        WorkflowAgentContextDto context, CancellationToken cancellationToken = default,
        Action<string, long>? onProgress = null)
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
            return await RunFormFillAsync(context, agent, providerCfg.BaseUrl, apiKey, model, cancellationToken, onProgress);
        }

        // ADR-0120: si este nodo de DECISION/COMPUERTA tiene un medio de comunicacion (linea de WhatsApp, agente
        // de voz, Colmena para web o permiso de correo), NO se decide a ciegas: se corre un bucle de herramientas
        // y el prompt empuja a USAR el medio (preguntarle al cliente el dato que falta) antes de rendirse. Sin
        // ningun medio, la ruta de un solo tiro de abajo queda intacta (cero cambio para los flujos que ya corren).
        var hasCommsMedium = context.Assignment?.WhatsAppLineId is not null
            || (context.Assignment?.VoiceAiAgentId is not null && context.VoiceCallResult is null)
            || context.Assignment?.ColmenaClientId is not null
            || context.Assignment?.CanSendEmail == true;
        if (hasCommsMedium)
        {
            return await RunDecisionWithToolsAsync(context, agent, providerCfg.BaseUrl, apiKey, model, cancellationToken, onProgress);
        }

        var systemPrompt = BuildSystemPrompt(agent.SystemPrompt, context);
        var userPrompt = WorkflowAgentContextSerializer.ToText(context);
        if (userPrompt.Length > MaxPromptChars)
        {
            userPrompt = userPrompt[..MaxPromptChars] + "\n[...contexto recortado...]";
        }

        // Progreso EN VIVO tambien en la ruta de UN solo tiro (nodos de decision/compuerta sin formulario):
        // antes solo el bucle de herramientas reportaba, asi que estos nodos se veian "trabajando" sin decir
        // nada. Ahora emiten al menos "leyendo/decidiendo" y, al terminar, la narracion o el motivo del modelo.
        onProgress?.Invoke(context.Node.NodeType == WorkflowNodeType.ExclusiveGateway
            ? "Leyendo el caso para elegir la ruta..."
            : "Leyendo el caso para decidir...", 0);

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
        var totalTokens = (long)response.InputTokens + response.OutputTokens;
        // Fase final legible: la narracion/decision del modelo (o el motivo si no pudo), con los tokens ya sumados.
        var finalPhase = parsed.Ok
            ? (Clip(parsed.Comment, 160)
               ?? (string.IsNullOrWhiteSpace(parsed.Result) ? "Decision tomada." : $"Decidio: {parsed.Result}"))
            : (Clip(parsed.Error, 160) ?? "No pudo decidir con los datos del caso.");
        onProgress?.Invoke(finalPhase!, totalTokens);

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
    private static void AppendExtraPrompt(StringBuilder sb, WorkflowAgentContextDto context)
    {
        // ADR-0093: instrucciones EXTRA de este paso. Se anteponen al resto del prompt de sistema para guiar
        // QUE hacer aqui y COMO usar las herramientas (ej. que abrir con Colmena y que extraer).
        var extra = context.Assignment?.ExtraPrompt;
        if (!string.IsNullOrWhiteSpace(extra))
        {
            sb.AppendLine("Instrucciones para este paso:");
            sb.AppendLine(extra!.Trim());
            sb.AppendLine();
        }
    }

    private static string BuildSystemPrompt(string agentPrompt, WorkflowAgentContextDto context)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(agentPrompt))
        {
            sb.AppendLine(agentPrompt.Trim());
            sb.AppendLine();
        }
        AppendExtraPrompt(sb, context);
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

    // ---- ADR-0120: DECIDIR/elegir ruta usando el medio de comunicacion del nodo (bucle de herramientas) ----

    /// <summary>Tope de rondas del bucle de decision: acota tokens y corta ciclos si el modelo no concluye.</summary>
    private const int MaxDecisionRounds = 6;

    /// <summary>
    /// Corre el bucle de function-calling para un nodo de DECISION o COMPUERTA que tiene un medio de
    /// comunicacion (ADR-0120). Ofrece las herramientas de comunicacion (web/llamada/WhatsApp/correo) y empuja
    /// a USARLAS para conseguir el dato que falta antes de decidir. El agente termina devolviendo el MISMO JSON
    /// de decision (`{puede_resolver, resultado|ruta, comentario}`) que la ruta de un solo tiro, asi que el
    /// parser y el runner no cambian. Si pide una llamada o un WhatsApp, el bucle corta y el runner pausa el
    /// paso (la respuesta lo reanuda con el dato en el contexto).
    /// </summary>
    private async Task<WorkflowAgentInvocationResult> RunDecisionWithToolsAsync(
        WorkflowAgentContextDto context, Domain.Entities.AiAgent agent, string? baseUrl, string apiKey, string model,
        CancellationToken cancellationToken, Action<string, long>? onProgress = null)
    {
        var canSearchWeb = context.Assignment?.ColmenaClientId is not null;
        var canCall = context.Assignment?.VoiceAiAgentId is not null && context.VoiceCallResult is null;
        var canAskWhatsApp = context.Assignment?.WhatsAppLineId is not null;
        var canSendEmail = context.Assignment?.CanSendEmail == true;

        // Mismas specs de comunicacion que el llenado (sin las herramientas de formulario): no hay formulario que
        // ver/fijar/enviar; la decision viaja como texto JSON en el ultimo turno del modelo.
        var tools = BuildDecisionTools(canSearchWeb, canCall, canAskWhatsApp, canSendEmail);
        var system = BuildDecisionWithToolsSystemPrompt(agent.SystemPrompt, context, canSearchWeb, canCall, canAskWhatsApp, canSendEmail);
        var userPrompt = WorkflowAgentContextSerializer.ToText(context);
        if (userPrompt.Length > MaxPromptChars) { userPrompt = userPrompt[..MaxPromptChars] + "\n[...contexto recortado...]"; }
        var messages = new List<AiToolMessage> { new("user", userPrompt) };

        var esCompuerta = context.Node.NodeType == WorkflowNodeType.ExclusiveGateway;
        string? decisionText = null;
        WorkflowAgentCallRequest? callRequest = null;
        WorkflowAgentWhatsAppRequest? whatsAppRequest = null;
        int? retryMinutes = null;
        var requestedTags = new List<string>();
        var emailsSent = 0;
        var docsSent = 0;
        var docNudged = false;        // red de seguridad de envio de documento: se fuerza una sola vez
        var resendNudged = false;     // red de seguridad de REENVIO pedido por el cliente: se fuerza una sola vez
        var impresionSentOk = false;  // true solo si 'enviar_impresion' devolvio ok (el PDF SI salio)
        // Respuesta FRESCA del cliente en esta corrida (para detectar si PIDIO que le reenvien el documento).
        var freshClientReply = context.WhatsAppReplyResult?.ReplyText;
        int inTokens = 0, outTokens = 0;

        onProgress?.Invoke(esCompuerta ? "Leyendo el caso para elegir la ruta..." : "Leyendo el caso para decidir...", 0);

        for (var round = 0; round < MaxDecisionRounds; round++)
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

            // Sin llamadas a herramientas: el modelo ya decidio -> su texto es el JSON de decision.
            if (completion.ToolCalls.Count == 0)
            {
                decisionText = completion.Text;
                break;
            }

            messages.Add(new AiToolMessage("assistant", completion.Text, completion.ToolCalls));
            onProgress?.Invoke(DescribeRound(completion.Text, completion.ToolCalls.Select(c => c.Name)), inTokens + outTokens);

            var pausing = false;
            foreach (var call in completion.ToolCalls)
            {
                string result;
                if (call.Name == "buscar_web" && canSearchWeb)
                {
                    result = await ExecuteWebSearchAsync(call.ArgumentsJson, context.Assignment!, agent.TenantId, cancellationToken);
                }
                else if (call.Name == "llamar_telefono" && canCall)
                {
                    callRequest = ReadCallRequest(call.ArgumentsJson);
                    pausing = true;
                    result = callRequest is null
                        ? """{"error": "falta 'numero' para la llamada"}"""
                        : """{"ok": true, "mensaje": "llamada solicitada; el paso quedara en espera del resultado"}""";
                }
                else if (call.Name == "preguntar_whatsapp" && canAskWhatsApp)
                {
                    whatsAppRequest = ReadWhatsAppRequest(call.ArgumentsJson);
                    pausing = true;
                    result = whatsAppRequest is null
                        ? """{"error": "faltan 'numero' o 'pregunta' para el WhatsApp"}"""
                        : """{"ok": true, "mensaje": "WhatsApp solicitado; el paso quedara en espera de la respuesta"}""";
                }
                else if (call.Name == "programar_reintento" && canAskWhatsApp)
                {
                    // ADR-0121: el agente se auto-reprograma. NO pausa por si mismo (acompana a la pregunta): solo
                    // registra en cuanto reintentar; el runner lo estampa en AgentNextRetryAt al pausar.
                    retryMinutes = ReadRetryMinutes(call.ArgumentsJson);
                    result = retryMinutes is null
                        ? """{"error": "falta 'en_minutos' (5..43200) para el reintento"}"""
                        : $$"""{"ok": true, "mensaje": "reintento programado en {{retryMinutes}} min si el cliente no responde"}""";
                }
                else if (call.Name == "enviar_correo" && canSendEmail)
                {
                    if (emailsSent >= MaxEmailsPerStep)
                    {
                        result = $$"""{"ok": false, "error": "ya se enviaron {{MaxEmailsPerStep}} correos en este paso (tope)"}""";
                    }
                    else
                    {
                        result = await ExecuteSendEmailAsync(call.ArgumentsJson, cancellationToken);
                        emailsSent++;
                    }
                }
                else if (call.Name == "etiquetar_tarea")
                {
                    // ADR-0124: el agente clasifica la tarea con el MOTIVO. Solo se acumula; el runner la aplica.
                    var tag = ReadTagName(call.ArgumentsJson);
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        result = """{"error": "falta 'etiqueta' (texto corto)"}""";
                    }
                    else
                    {
                        if (requestedTags.Count < MaxTagsPerStep
                            && !requestedTags.Contains(tag!, StringComparer.OrdinalIgnoreCase)) { requestedTags.Add(tag!); }
                        result = $$"""{"ok": true, "mensaje": "etiqueta '{{tag}}' marcada para la tarea"}""";
                    }
                }
                else if (call.Name == "listar_formularios" && canAskWhatsApp)
                {
                    result = await ExecuteListFormsAsync(cancellationToken);
                }
                else if (call.Name == "enviar_impresion" && canAskWhatsApp)
                {
                    if (docsSent >= MaxDocsPerStep)
                    {
                        result = $$"""{"ok": false, "error": "ya se enviaron {{MaxDocsPerStep}} documentos en este paso (tope)"}""";
                    }
                    else
                    {
                        result = await ExecuteSendImpresionAsync(call.ArgumentsJson, context, cancellationToken);
                        docsSent++;
                        try
                        {
                            using var okDoc = JsonDocument.Parse(result);
                            if (okDoc.RootElement.ValueKind == JsonValueKind.Object
                                && okDoc.RootElement.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True)
                            {
                                impresionSentOk = true;
                            }
                        }
                        catch (JsonException) { /* si no parsea, se trata como no-ok */ }
                    }
                    // El resultado del envio del documento (OK o el motivo del fallo) se deja en la bitacora del
                    // paso: asi un "no llego el documento" deja de ser invisible y se ve si el agente lo envio o no.
                    onProgress?.Invoke(DescribeImpresionOutcome(result), inTokens + outTokens);
                }
                else
                {
                    result = $$"""{"error": "herramienta '{{call.Name}}' no disponible"}""";
                }
                messages.Add(new AiToolMessage("tool", result, ToolCallId: call.Id, ToolName: call.Name));
            }

            // RED DE SEGURIDAD DE ENVIO DE DOCUMENTO: si el agente va a PAUSAR mandando un WhatsApp que AFIRMA que
            // ya reenvio/adjunto la cotizacion u otro documento, pero NO llamo 'enviar_impresion' en esta corrida, es
            // una PROMESA VACIA (el cliente nunca recibe el PDF). Se le fuerza UNA vez: en vez de pausar, se le pide
            // llamar 'enviar_impresion' ahora. Si de verdad no iba a enviar nada, puede reformular sin afirmarlo.
            if (pausing && whatsAppRequest is not null && !impresionSentOk && !docNudged
                && ClaimsDocumentSendWithoutTool(whatsAppRequest.Pregunta))
            {
                docNudged = true;
                pausing = false;
                whatsAppRequest = null;
                messages.Add(new AiToolMessage("user",
                    "[SISTEMA] Afirmaste al cliente que le reenviaste/adjuntaste la cotizacion o un documento, pero NO " +
                    "invocaste la herramienta 'enviar_impresion' en esta corrida: eso seria una PROMESA VACIA (el cliente " +
                    "NO recibe nada). Es OBLIGATORIO: si vas a decirle que le enviaste el documento, LLAMA AHORA " +
                    "'enviar_impresion' con el formulario correcto (usa 'listar_formularios' si no sabes el nombre). Si NO " +
                    "ibas a enviar un documento, reformula tu mensaje sin afirmar que lo enviaste."));
                onProgress?.Invoke("Red de seguridad: afirmo enviar el documento sin hacerlo; se le pide enviarlo.", inTokens + outTokens);
                continue;
            }

            // RED DE SEGURIDAD DE REENVIO PEDIDO POR EL CLIENTE: si en su mensaje FRESCO el cliente pidio que le
            // reenvien la cotizacion/documento (lo perdio, lo borro, no le llego, "mandamela de nuevo") y el agente
            // va a PAUSAR preguntandole algo (tipico: "confirmame si la recibiste") SIN haber llamado
            // 'enviar_impresion' en esta corrida, el documento NO sale. Se le fuerza UNA vez a reenviarlo de verdad.
            if (pausing && whatsAppRequest is not null && !impresionSentOk && !resendNudged
                && AsksToResendDocument(freshClientReply))
            {
                resendNudged = true;
                pausing = false;
                whatsAppRequest = null;
                messages.Add(new AiToolMessage("user",
                    "[SISTEMA] El cliente PIDIO que le REENVIES la cotizacion/documento (dijo que lo perdio, lo borro, no " +
                    "le llego, o que se lo mandes de nuevo). NO basta con preguntarle si lo recibio ni prometerle que ya " +
                    "se lo envias: es OBLIGATORIO llamar AHORA 'enviar_impresion' para reenviarselo de verdad (usa " +
                    "'listar_formularios' si no sabes el nombre del formulario). Solo DESPUES de enviarlo, confirma con " +
                    "'preguntar_whatsapp'."));
                onProgress?.Invoke("Red de seguridad: el cliente pidio reenvio y no se llamo enviar_impresion; se fuerza el envio.", inTokens + outTokens);
                continue;
            }

            // Una llamada o un WhatsApp PAUSAN el paso: el runner los coloca/envia y el paso espera la respuesta.
            if (pausing) { break; }
        }

        // El agente pidio una llamada -> el runner la coloca y pausa el paso.
        if (callRequest is not null)
        {
            return new WorkflowAgentInvocationResult(
                true, Result: null, Comment: null, Error: null,
                agent.Provider, model, inTokens, outTokens, Route: null, Fields: null, CallRequest: callRequest,
                RetryInMinutes: retryMinutes, RequestedTagNames: requestedTags.Count > 0 ? requestedTags : null);
        }
        // El agente pidio preguntar por WhatsApp -> el runner envia y pausa el paso.
        if (whatsAppRequest is not null)
        {
            return new WorkflowAgentInvocationResult(
                true, Result: null, Comment: null, Error: null,
                agent.Provider, model, inTokens, outTokens, Route: null, Fields: null, WhatsAppRequest: whatsAppRequest,
                RetryInMinutes: retryMinutes, RequestedTagNames: requestedTags.Count > 0 ? requestedTags : null);
        }
        if (string.IsNullOrWhiteSpace(decisionText))
        {
            return WorkflowAgentInvocationResult.Failed(
                "El agente no concluyo una decision tras usar sus herramientas.", agent.Provider, model, inTokens, outTokens);
        }

        var parsed = WorkflowAgentDecisionParser.Parse(decisionText!);
        var totalTokens = (long)inTokens + outTokens;
        var finalPhase = parsed.Ok
            ? (Clip(parsed.Comment, 160) ?? (string.IsNullOrWhiteSpace(parsed.Result) ? "Decision tomada." : $"Decidio: {parsed.Result}"))
            : (Clip(parsed.Error, 160) ?? "No pudo decidir con los datos del caso.");
        onProgress?.Invoke(finalPhase!, totalTokens);

        return parsed with
        {
            Provider = agent.Provider,
            Model = model,
            InputTokens = inTokens,
            OutputTokens = outTokens,
            RequestedTagNames = requestedTags.Count > 0 ? requestedTags : null
        };
    }

    /// <summary>Tope de etiquetas (motivo) que el agente puede pedir en un paso (ADR-0124), por costo/ruido.</summary>
    private const int MaxTagsPerStep = 4;

    /// <summary>Lee 'etiqueta' (texto corto) de la herramienta 'etiquetar_tarea' (ADR-0124). Null/recorta si no es valido.</summary>
    private static string? ReadTagName(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) { return null; }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            if (!doc.RootElement.TryGetProperty("etiqueta", out var el) || el.ValueKind != System.Text.Json.JsonValueKind.String) { return null; }
            var s = el.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(s)) { return null; }
            return s!.Length > 60 ? s[..60] : s;
        }
        catch { return null; }
    }

    /// <summary>Spec de la herramienta 'etiquetar_tarea' (ADR-0124): clasifica la tarea con el motivo del caso.</summary>
    private static AiToolSpec BuildTagTool()
        => new("etiquetar_tarea",
            "Agrega una ETIQUETA (texto corto) a la tarea para clasificar el motivo o el resultado del caso, p.ej. 'Precio alto', 'Se fue con la competencia', 'Interesado'. Prefiere reusar una de las etiquetas existentes del tenant que aparecen en el contexto. Puedes llamarla varias veces para poner mas de una.",
            """{"type":"object","properties":{"etiqueta":{"type":"string","description":"Nombre corto de la etiqueta (<=60 chars)."}},"required":["etiqueta"]}""");

    /// <summary>Herramientas de COMUNICACION para un nodo de decision/compuerta (ADR-0120): las mismas del
    /// llenado menos las de formulario. El medio disponible decide cuales se ofrecen.</summary>
    private static IReadOnlyList<AiToolSpec> BuildDecisionTools(bool canSearchWeb, bool canCall, bool canAskWhatsApp, bool canSendEmail)
    {
        var tools = new List<AiToolSpec>();
        if (canSearchWeb)
        {
            tools.Add(new AiToolSpec("buscar_web",
                "Abre una pagina web y extrae su contenido para conseguir un dato que falta. 'url' obligatorio; 'objetivo' describe que buscar.",
                """{"type":"object","properties":{"url":{"type":"string"},"objetivo":{"type":"string"}},"required":["url"]}"""));
        }
        if (canCall)
        {
            tools.Add(new AiToolSpec("llamar_telefono",
                "Coloca una llamada para conseguir un dato con una persona. 'numero' obligatorio (con codigo de pais). El paso quedara EN ESPERA del resultado y luego retomaras la decision.",
                """{"type":"object","properties":{"numero":{"type":"string"},"objetivo":{"type":"string"}},"required":["numero"]}"""));
        }
        if (canAskWhatsApp)
        {
            tools.Add(new AiToolSpec("preguntar_whatsapp",
                "Envia UNA pregunta por WhatsApp para CONSEGUIR o confirmar un dato que necesitas para decidir. 'numero' obligatorio (con codigo de pais, ej. +57...); 'pregunta' es el texto que se envia. El paso quedara EN ESPERA de la respuesta y luego retomaras la decision. Usala si el dato depende del cliente y no esta en el contexto.",
                """{"type":"object","properties":{"numero":{"type":"string"},"pregunta":{"type":"string"}},"required":["numero","pregunta"]}"""));
            tools.Add(BuildRetryTool());
            tools.Add(BuildListFormsTool());
            tools.Add(BuildSendImpresionTool());
        }
        if (canSendEmail)
        {
            tools.Add(new AiToolSpec("enviar_correo",
                "Envia un correo. 'para' (email) y 'asunto' y 'cuerpo' obligatorios.",
                """{"type":"object","properties":{"para":{"type":"string"},"asunto":{"type":"string"},"cuerpo":{"type":"string"}},"required":["para","asunto","cuerpo"]}"""));
        }
        // ADR-0124: etiquetar la tarea con el motivo (siempre disponible; el runner aplica lo que el agente pida).
        tools.Add(BuildTagTool());
        return tools;
    }

    /// <summary>Prompt de sistema para la decision CON herramientas (ADR-0120): el contrato de decision de
    /// siempre + el empujon a USAR el medio de comunicacion disponible antes de rendirse.</summary>
    private static string BuildDecisionWithToolsSystemPrompt(
        string agentPrompt, WorkflowAgentContextDto context, bool canSearchWeb, bool canCall, bool canAskWhatsApp, bool canSendEmail)
    {
        var sb = new StringBuilder(BuildSystemPrompt(agentPrompt, context));
        sb.AppendLine();
        sb.AppendLine("Tienes herramientas para CONSEGUIR lo que te falte antes de decidir:");
        if (canAskWhatsApp) { sb.AppendLine("- 'preguntar_whatsapp': pregunta al cliente por WhatsApp (el paso espera la respuesta y la recibiras para decidir)."); }
        if (canAskWhatsApp) { sb.AppendLine("- 'programar_reintento': cuando le preguntes al cliente, PROGRAMA tambien un reintento (en_minutos) por si no responde; al cumplirse, volveras a correr para enviarle un recordatorio. Si responde antes, se cancela solo."); }
        if (canAskWhatsApp) { sb.AppendLine("- 'listar_formularios': lista los formularios del caso y las plantillas de impresion que puedes reenviar al cliente (ej. la cotizacion)."); }
        if (canAskWhatsApp) { sb.AppendLine("- 'enviar_impresion': ENVIA de verdad al cliente, por WhatsApp, el PDF de un formulario del caso (ej. la cotizacion). Es la UNICA forma de hacer llegar el documento; sin esta herramienta el cliente NO recibe nada."); }
        if (canCall) { sb.AppendLine("- 'llamar_telefono': coloca una llamada para conseguir el dato."); }
        if (canSearchWeb) { sb.AppendLine("- 'buscar_web': abre una pagina y extrae un dato."); }
        if (canSendEmail) { sb.AppendLine("- 'enviar_correo': envia un correo."); }
        sb.AppendLine();
        sb.AppendLine("IMPORTANTE: se te dio un medio de comunicacion con el cliente; lo evidente es USARLO para llegar a la decision.");
        sb.AppendLine("Si el dato que necesitas depende del cliente y no esta en el contexto, PREGUNTASELO por ese medio en vez de responder puede_resolver=false.");
        if (canAskWhatsApp)
        {
            sb.AppendLine();
            sb.AppendLine("ENVIAR DOCUMENTOS AL CLIENTE: si el cliente pide la cotizacion o cualquier documento del caso -o que se lo REENVIES porque lo borro o no lo vio- tu accion OBLIGATORIA es llamar 'enviar_impresion' para mandarselo de verdad. PROHIBIDO decir 'ya te lo envio', 'te lo mando', 'permiteme un momento mientras lo recupero' o cualquier promesa de envio SIN haber llamado 'enviar_impresion' en este MISMO turno: prometer sin enviar es un error grave (el cliente se queda esperando un documento que nunca llega). Si no sabes el nombre exacto del formulario o de la plantilla, llama primero 'listar_formularios'. Si 'enviar_impresion' responde ok=false, NO afirmes que ya lo enviaste: reintenta con el nombre correcto o avisale al cliente del problema con sinceridad.");
            sb.AppendLine("DESPUES DE ENVIAR EL DOCUMENTO: 'enviar_impresion' ya hace llegar el PDF; NO termines el turno en seco despues de llamarla. Confirma al cliente con 'preguntar_whatsapp' (ej. 'Te acabo de reenviar la cotizacion; cualquier duda me dices. Deseas que avancemos con la compra?'), lo que ademas deja el paso EN ESPERA de su respuesta para seguir atendiendolo. Nunca cierres la corrida justo despues de 'enviar_impresion' sin una accion valida (ni ruta, ni resultado, ni pregunta): si lo haces, el paso se devuelve a una persona y se pierde la atencion automatica.");
        }
        if (context.Node.NodeType == WorkflowNodeType.ExclusiveGateway && canAskWhatsApp)
        {
            sb.AppendLine();
            sb.AppendLine("REGLA CLAVE (esta COMPUERTA espera la respuesta del cliente): el SILENCIO del cliente NO es una decision. NUNCA elijas una ruta mientras el cliente NO haya respondido. Si el cliente aun no respondio, tu unica accion valida es 'preguntar_whatsapp' para enviarle un recordatorio; si ya insististe lo suficiente, responde puede_resolver=false para SEGUIR ESPERANDO. JAMAS cierres como 'no desea comprar' (ni ninguna ruta) por falta de respuesta. Elige una ruta SOLO cuando el cliente de una respuesta CLARA.");
        }
        sb.AppendLine("Solo cuando YA tengas lo necesario (o sea imposible conseguirlo), responde el JSON de decision descrito arriba, sin llamar mas herramientas.");
        return sb.ToString();
    }

    // ---- ADR-0090 ola C: LLENAR el formulario del paso con tool-calling (ver/fijar/enviar) ----

    /// <summary>Tope de rondas del bucle de llenado: ver/fijar/enviar no necesitan muchas vueltas; acota tokens.</summary>
    private const int MaxFormRounds = 8;

    /// <summary>Cuantas 'buscar_web' de un MISMO turno se corren a la vez. La Colmena abre un navegador
    /// aislado por orden (WebView2BrowserSubAgent) y el canal es concurrente por correlationId, asi que el
    /// paralelismo es real; el tope acota la RAM de la maquina Colmena (cada navegador ~= un Chromium).</summary>
    private const int MaxParallelWebSearches = 3;

    /// <summary>Corre el bucle de function-calling para diligenciar el formulario del paso. ACUMULA los valores
    /// en memoria (este servicio NO escribe BD); el runner los envia por SaveAsync con la validacion real. El
    /// modelo termina llamando 'enviar_formulario'; si no lo hace, es "no pudo" y el paso vuelve a una persona.</summary>
    private async Task<WorkflowAgentInvocationResult> RunFormFillAsync(
        WorkflowAgentContextDto context, Domain.Entities.AiAgent agent, string? baseUrl, string apiKey, string model,
        CancellationToken cancellationToken, Action<string, long>? onProgress = null)
    {
        var form = context.Node.Form!;
        var canSearchWeb = context.Assignment?.ColmenaClientId is not null;
        // Solo se ofrece llamar si hay agente de voz Y no venimos ya de una llamada (reanudacion): en la
        // reanudacion el agente debe USAR el resultado y terminar, no encadenar otra llamada.
        var canCall = context.Assignment?.VoiceAiAgentId is not null && context.VoiceCallResult is null;
        // ADR-0092: WhatsApp SI se sigue ofreciendo en la reanudacion (es multi-turno: el agente puede
        // repreguntar si la respuesta no basta). El tope de preguntas lo pone el runner, no aqui.
        var canAskWhatsApp = context.Assignment?.WhatsAppLineId is not null;
        // ADR-0093: enviar correo (sincrono, sin pausa). Permiso explicito por nodo.
        var canSendEmail = context.Assignment?.CanSendEmail == true;
        var tools = BuildFormTools(canSearchWeb, canCall, canAskWhatsApp, canSendEmail);
        var system = BuildFormSystemPrompt(agent.SystemPrompt, context, canSearchWeb, canCall, canAskWhatsApp, canSendEmail);
        var userPrompt = WorkflowAgentContextSerializer.ToText(context);
        if (userPrompt.Length > MaxPromptChars) { userPrompt = userPrompt[..MaxPromptChars] + "\n[...contexto recortado...]"; }
        var messages = new List<AiToolMessage> { new("user", userPrompt) };

        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? finalComment = null;
        var finished = false;
        WorkflowAgentCallRequest? callRequest = null;
        WorkflowAgentWhatsAppRequest? whatsAppRequest = null;
        int? retryMinutes = null;
        var requestedTags = new List<string>();
        var emailsSent = 0;
        var docsSent = 0;
        int inTokens = 0, outTokens = 0;

        // Capa 2 (ADR-0091): primer latido de progreso, antes de la primera llamada al proveedor.
        onProgress?.Invoke("Analizando el requerimiento...", 0);

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

            // Capa 2 (ADR-0091): latido de progreso EN VIVO. La "fase" es lo que el agente dice/hace en esta
            // ronda (su propio texto si lo hay, o una etiqueta derivada de las herramientas que invoco); los
            // tokens son el acumulado hasta aqui. Best-effort/decorativo (el runner lo transmite por SignalR).
            onProgress?.Invoke(
                DescribeRound(completion.Text, completion.ToolCalls.Select(c => c.Name)),
                inTokens + outTokens);

            // ADR-0091 (paralelo): la Colmena ya abre un navegador aislado por orden y el canal es
            // concurrente por correlationId; lo unico que serializaba era este bucle. Si el modelo pide
            // VARIAS 'buscar_web' en un mismo turno, se lanzan a la vez (acotadas por MaxParallelWebSearches)
            // y se cosechan EN ORDEN mas abajo. Solo buscar_web se paraleliza: no toca 'fields'/'finished'
            // ni pausa el paso, asi que no hay carrera. El resto de herramientas sigue estrictamente en fila.
            Dictionary<string, Task<string>>? webTasks = null;
            if (canSearchWeb)
            {
                var webCalls = completion.ToolCalls.Where(c => c.Name == "buscar_web").ToList();
                if (webCalls.Count > 1)
                {
                    using var gate = new SemaphoreSlim(MaxParallelWebSearches);
                    async Task<string> RunGatedAsync(string argsJson)
                    {
                        await gate.WaitAsync(cancellationToken);
                        try { return await ExecuteWebSearchAsync(argsJson, context.Assignment!, agent.TenantId, cancellationToken); }
                        finally { gate.Release(); }
                    }
                    webTasks = new Dictionary<string, Task<string>>(StringComparer.Ordinal);
                    foreach (var wc in webCalls) { webTasks[wc.Id] = RunGatedAsync(wc.ArgumentsJson); }
                    // WhenAll dentro del 'using': el semaforo no se libera hasta que todas terminaron.
                    await Task.WhenAll(webTasks.Values);
                }
            }

            foreach (var call in completion.ToolCalls)
            {
                string result;
                if (call.Name == "buscar_web" && canSearchWeb)
                {
                    // ADR-0091: el agente busca un dato en la web con el cliente Colmena del nodo (sincrono,
                    // acotado). El invoker no escribe BD: solo trae el contenido para que el agente lo use.
                    // Si venia en un lote paralelo ya esta resuelto (Task completado); si no, se ejecuta aqui.
                    result = webTasks is not null && webTasks.TryGetValue(call.Id, out var wt)
                        ? await wt
                        : await ExecuteWebSearchAsync(call.ArgumentsJson, context.Assignment!, agent.TenantId, cancellationToken);
                }
                else if (call.Name == "llamar_telefono" && canCall)
                {
                    // ADR-0091: el agente pide una llamada. NO se coloca aqui (asincrona): se registra y el
                    // bucle termina; el runner coloca la llamada y pausa el paso hasta que llegue el resultado.
                    callRequest = ReadCallRequest(call.ArgumentsJson);
                    finished = true;
                    result = callRequest is null
                        ? """{"error": "falta 'numero' para la llamada"}"""
                        : """{"ok": true, "mensaje": "llamada solicitada; el paso quedara en espera del resultado"}""";
                }
                else if (call.Name == "preguntar_whatsapp" && canAskWhatsApp)
                {
                    // ADR-0092: el agente pide preguntar por WhatsApp. NO se envia aqui (asincrono): se registra
                    // y el bucle termina; el runner envia y pausa el paso hasta que llegue la respuesta.
                    whatsAppRequest = ReadWhatsAppRequest(call.ArgumentsJson);
                    finished = true;
                    result = whatsAppRequest is null
                        ? """{"error": "faltan 'numero' o 'pregunta' para el WhatsApp"}"""
                        : """{"ok": true, "mensaje": "WhatsApp solicitado; el paso quedara en espera de la respuesta"}""";
                }
                else if (call.Name == "programar_reintento" && canAskWhatsApp)
                {
                    // ADR-0121: el agente se auto-reprograma (acompana a la pregunta; no pausa por si mismo). El
                    // runner lo estampa en AgentNextRetryAt al pausar por el WhatsApp.
                    retryMinutes = ReadRetryMinutes(call.ArgumentsJson);
                    result = retryMinutes is null
                        ? """{"error": "falta 'en_minutos' (5..43200) para el reintento"}"""
                        : $$"""{"ok": true, "mensaje": "reintento programado en {{retryMinutes}} min si el cliente no responde"}""";
                }
                else if (call.Name == "enviar_correo" && canSendEmail)
                {
                    // ADR-0093: el agente envia un correo (sincrono, sin pausa; el correo entrante no existe).
                    // Tope por paso como red de seguridad anti-bucle.
                    if (emailsSent >= MaxEmailsPerStep)
                    {
                        result = $$"""{"ok": false, "error": "ya se enviaron {{MaxEmailsPerStep}} correos en este paso (tope)"}""";
                    }
                    else
                    {
                        result = await ExecuteSendEmailAsync(call.ArgumentsJson, cancellationToken);
                        emailsSent++;
                    }
                }
                else if (call.Name == "etiquetar_tarea")
                {
                    // ADR-0124: el agente clasifica la tarea con el motivo. Solo se acumula; el runner la aplica.
                    var tag = ReadTagName(call.ArgumentsJson);
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        result = """{"error": "falta 'etiqueta' (texto corto)"}""";
                    }
                    else
                    {
                        if (requestedTags.Count < MaxTagsPerStep
                            && !requestedTags.Contains(tag!, StringComparer.OrdinalIgnoreCase)) { requestedTags.Add(tag!); }
                        result = $$"""{"ok": true, "mensaje": "etiqueta '{{tag}}' marcada para la tarea"}""";
                    }
                }
                else if (call.Name == "listar_formularios" && canAskWhatsApp)
                {
                    result = await ExecuteListFormsAsync(cancellationToken);
                }
                else if (call.Name == "enviar_impresion" && canAskWhatsApp)
                {
                    if (docsSent >= MaxDocsPerStep)
                    {
                        result = $$"""{"ok": false, "error": "ya se enviaron {{MaxDocsPerStep}} documentos en este paso (tope)"}""";
                    }
                    else
                    {
                        result = await ExecuteSendImpresionAsync(call.ArgumentsJson, context, cancellationToken);
                        docsSent++;
                    }
                    onProgress?.Invoke(DescribeImpresionOutcome(result), inTokens + outTokens);
                }
                else
                {
                    result = call.Name switch
                    {
                        "ver_formulario" => FormSchemaJson(form),
                        "fijar_campos" => ApplySetFields(call.ArgumentsJson, form, fields),
                        "enviar_formulario" => MarkFinished(call.ArgumentsJson, ref finished, ref finalComment),
                        _ => $$"""{"error": "herramienta '{{call.Name}}' no disponible"}"""
                    };
                }
                messages.Add(new AiToolMessage("tool", result, ToolCallId: call.Id, ToolName: call.Name));
            }
        }

        // ADR-0091: el agente pidio una llamada -> el runner la coloca y pausa el paso (los campos ya fijados,
        // si hay, se conservan para completar el llenado cuando la llamada regrese con el dato faltante).
        if (callRequest is not null)
        {
            return new WorkflowAgentInvocationResult(
                true, Result: null, Comment: Clip(finalComment, 2000), Error: null,
                agent.Provider, model, inTokens, outTokens, Route: null,
                Fields: fields.Count > 0 ? fields : null, CallRequest: callRequest, RetryInMinutes: retryMinutes,
                RequestedTagNames: requestedTags.Count > 0 ? requestedTags : null);
        }

        // ADR-0092: el agente pidio preguntar por WhatsApp -> el runner envia y pausa el paso; los campos ya
        // fijados se conservan para completar el llenado cuando llegue la respuesta.
        if (whatsAppRequest is not null)
        {
            return new WorkflowAgentInvocationResult(
                true, Result: null, Comment: Clip(finalComment, 2000), Error: null,
                agent.Provider, model, inTokens, outTokens, Route: null,
                Fields: fields.Count > 0 ? fields : null, WhatsAppRequest: whatsAppRequest, RetryInMinutes: retryMinutes,
                RequestedTagNames: requestedTags.Count > 0 ? requestedTags : null);
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
            agent.Provider, model, inTokens, outTokens, Route: null, Fields: fields,
            RequestedTagNames: requestedTags.Count > 0 ? requestedTags : null);
    }

    private static IReadOnlyList<AiToolSpec> BuildFormTools(bool canSearchWeb, bool canCall, bool canAskWhatsApp, bool canSendEmail)
    {
        var tools = new List<AiToolSpec>
        {
            new("ver_formulario",
                "Devuelve el esquema del formulario del paso: campos con codigo, etiqueta, tipo, si es obligatorio y sus opciones.",
                """{"type":"object","properties":{}}"""),
            new("fijar_campos",
                "Fija valores del formulario. 'campos' es un objeto {codigo_de_campo: valor}. Puedes llamarla varias veces; se acumulan. Para un campo TABLA (tipo GridDetail) el valor es un ARREGLO de filas: [{codigo_de_columna: valor, ...}, ...], usando solo las columnas de 'columnas' del esquema.",
                """{"type":"object","properties":{"campos":{"type":"object"}},"required":["campos"]}"""),
            new("enviar_formulario",
                "Marca el formulario como LISTO cuando ya fijaste todos los campos obligatorios. Acepta 'comentario' opcional.",
                """{"type":"object","properties":{"comentario":{"type":"string"}}}"""),
        };
        if (canSearchWeb)
        {
            // ADR-0091: buscar un dato en la web con el navegador Colmena del nodo.
            tools.Add(new AiToolSpec("buscar_web",
                "Abre una URL con el navegador Colmena y devuelve su contenido legible, para CONSEGUIR un dato que no esta en el contexto. 'url' obligatoria (http/https); 'selector' CSS opcional. Si necesitas consultar VARIAS paginas, pide todas las 'buscar_web' JUNTAS en el mismo turno: se abren en navegadores separados a la vez y es mucho mas rapido.",
                """{"type":"object","properties":{"url":{"type":"string"},"selector":{"type":"string"}},"required":["url"]}"""));
        }
        if (canCall)
        {
            // ADR-0091: pedir una llamada telefonica para conseguir un dato. La llamada es asincrona: el paso
            // queda EN ESPERA y el agente retoma cuando el resultado llegue. Llamala UNA sola vez y al final.
            tools.Add(new AiToolSpec("llamar_telefono",
                "Solicita una llamada telefonica (voz IA) para CONSEGUIR un dato faltante (ej. confirmar un telefono o correo con el cliente). 'numero' obligatorio (E.164, ej. +57...); 'objetivo' describe que dato conseguir. El paso quedara EN ESPERA del resultado de la llamada.",
                """{"type":"object","properties":{"numero":{"type":"string"},"objetivo":{"type":"string"}},"required":["numero"]}"""));
        }
        if (canAskWhatsApp)
        {
            // ADR-0092: preguntar por WhatsApp para conseguir un dato. Asincrono: el paso queda EN ESPERA de la
            // respuesta y el agente retoma con ella; puede volver a preguntar si hace falta (con moderacion).
            tools.Add(new AiToolSpec("preguntar_whatsapp",
                "Envia UNA pregunta por WhatsApp para CONSEGUIR o confirmar un dato faltante con una persona. 'numero' obligatorio (con codigo de pais, ej. +57...); 'pregunta' es el texto que se le envia. El paso quedara EN ESPERA de la respuesta y luego retomaras el llenado. Usala solo si el dato no esta en el contexto ni lo consigues por web.",
                """{"type":"object","properties":{"numero":{"type":"string"},"pregunta":{"type":"string"}},"required":["numero","pregunta"]}"""));
            tools.Add(BuildRetryTool());
            tools.Add(BuildListFormsTool());
            tools.Add(BuildSendImpresionTool());
        }
        if (canSendEmail)
        {
            // ADR-0093: enviar un correo (sincrono, sin esperar respuesta). El agente redacta asunto+cuerpo.
            tools.Add(new AiToolSpec("enviar_correo",
                "Envia un correo electronico (para avisar, pedir o confirmar algo). 'destinatario' obligatorio (email); 'asunto' y 'cuerpo' obligatorios (los redactas tu con lo que sabes del caso). NO espera respuesta: es solo un envio. El remitente lo fija la empresa.",
                """{"type":"object","properties":{"destinatario":{"type":"string"},"asunto":{"type":"string"},"cuerpo":{"type":"string"}},"required":["destinatario","asunto","cuerpo"]}"""));
        }
        // ADR-0124: etiquetar la tarea con el motivo (siempre disponible).
        tools.Add(BuildTagTool());
        return tools;
    }

    /// <summary>Parte B: lista formularios del tenant + impresiones (plantillas) disponibles, por NOMBRE. Solo
    /// lectura. El prompt del nodo decide que esta autorizado a reenviar.</summary>
    private static AiToolSpec BuildListFormsTool()
        => new("listar_formularios",
            "Lista los FORMULARIOS del tenant y las IMPRESIONES (plantillas de impresion) disponibles, por NOMBRE. " +
            "Usala para saber que puedes reenviar al cliente (ej. la cotizacion) antes de llamar 'enviar_impresion'. " +
            "Solo lectura. Reenvia unicamente lo que te AUTORICEN las instrucciones de este paso.",
            """{"type":"object","properties":{}}""");

    /// <summary>Parte B: envia al CLIENTE del caso la impresion (PDF) de un formulario de ESTE caso. El
    /// destinatario es fijo (el cliente de la tarea); el prompt del nodo decide que formulario/impresion esta
    /// autorizado a mandar.</summary>
    private static AiToolSpec BuildSendImpresionTool()
        => new("enviar_impresion",
            "Envia al CLIENTE de este caso, por WhatsApp, la IMPRESION (PDF) de un formulario de ESTE caso (ej. " +
            "reenviar la cotizacion si el cliente la pide de nuevo). 'formulario' = nombre o codigo del formulario " +
            "(ver listar_formularios); 'impresion' = nombre de la plantilla de impresion (opcional; por defecto la " +
            "predeterminada); 'mensaje' = texto corto opcional que acompana el archivo. El destinatario es SIEMPRE el " +
            "cliente de la tarea (no puedes elegir otro numero). Envia SOLO lo que te AUTORICEN las instrucciones de " +
            "este paso: nunca mandes documentos ni datos que no esten autorizados aqui.",
            """{"type":"object","properties":{"formulario":{"type":"string"},"impresion":{"type":"string"},"mensaje":{"type":"string"}},"required":["formulario"]}""");

    /// <summary>ADR-0121: herramienta para que el agente se AUTO-REPROGRAME (reintento por silencio del cliente).
    /// Se usa JUNTO con 'preguntar_whatsapp': "le escribo ahora y me reprogramo en X por si no responde".</summary>
    private static AiToolSpec BuildRetryTool()
        => new("programar_reintento",
            "Programa TU PROPIO siguiente intento por si el cliente no responde a tu pregunta. 'en_minutos' es cuanto esperar antes de reintentar (ej. 1440 = 1 dia; min 5, max 43200). Usala en el MISMO turno que 'preguntar_whatsapp'. Si llega esa hora y el cliente sigue sin responder, volveras a correr con el contexto actualizado para enviarle un recordatorio (o rendirte si ya insististe). Si el cliente responde antes, el reintento se cancela solo.",
            """{"type":"object","properties":{"en_minutos":{"type":"integer"}},"required":["en_minutos"]}""");

    /// <summary>Lee 'en_minutos' de 'programar_reintento', acotado a [5, 43200] (30 dias). Null si no es valido.</summary>
    private static int? ReadRetryMinutes(string argsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            if (doc.RootElement.TryGetProperty("en_minutos", out var m))
            {
                int? val = m.ValueKind switch
                {
                    JsonValueKind.Number when m.TryGetInt32(out var i) => i,
                    JsonValueKind.String when int.TryParse(m.GetString(), out var i) => i,
                    _ => null
                };
                if (val is int v) { return Math.Clamp(v, 5, 43200); }
            }
            return null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Lee los argumentos de 'preguntar_whatsapp'. Null si falta el numero o la pregunta.</summary>
    private static WorkflowAgentWhatsAppRequest? ReadWhatsAppRequest(string argsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            var root = doc.RootElement;
            var numero = root.TryGetProperty("numero", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            var pregunta = root.TryGetProperty("pregunta", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            if (string.IsNullOrWhiteSpace(numero) || string.IsNullOrWhiteSpace(pregunta)) { return null; }
            return new WorkflowAgentWhatsAppRequest(numero!.Trim(), Clip(pregunta, 1500)!);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Lee los argumentos de 'llamar_telefono'. Null si falta el numero.</summary>
    private static WorkflowAgentCallRequest? ReadCallRequest(string argsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            var root = doc.RootElement;
            var numero = root.TryGetProperty("numero", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(numero)) { return null; }
            var objetivo = root.TryGetProperty("objetivo", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null;
            return new WorkflowAgentCallRequest(numero!.Trim(), Clip(objetivo, 1000));
        }
        catch (JsonException) { return null; }
    }

    private static string BuildFormSystemPrompt(string agentPrompt, WorkflowAgentContextDto context, bool canSearchWeb, bool canCall, bool canAskWhatsApp, bool canSendEmail)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(agentPrompt)) { sb.AppendLine(agentPrompt.Trim()); sb.AppendLine(); }
        AppendExtraPrompt(sb, context);
        sb.AppendLine("Atiendes un paso de un proceso de negocio que exige DILIGENCIAR un formulario.");
        sb.AppendLine("Herramientas: 'ver_formulario' (esquema), 'fijar_campos' (pon valores con {\"campos\":{codigo:valor}}), 'enviar_formulario' (marca LISTO al terminar).");
        if (canSearchWeb)
        {
            sb.AppendLine("Ademas tienes 'buscar_web' (abre una URL con el navegador Colmena y te devuelve su contenido) para CONSEGUIR un dato que falte antes de fijarlo.");
            sb.AppendLine("Si vas a consultar varias fuentes, PIDELAS TODAS EN EL MISMO TURNO (varias 'buscar_web' a la vez): la Colmena las abre en navegadores separados en paralelo y respondes mas rapido. No las hagas de a una si puedes lanzarlas juntas.");
        }
        if (canCall)
        {
            sb.AppendLine("Ademas tienes 'llamar_telefono' para CONSEGUIR un dato por una llamada de voz (ej. confirmar telefono/correo). Usala solo si el dato no esta en el contexto ni lo consigues por web; el paso quedara en espera del resultado de la llamada y luego retomaras el llenado.");
        }
        if (canAskWhatsApp)
        {
            sb.AppendLine("Ademas tienes 'preguntar_whatsapp' para CONSEGUIR un dato preguntandole a una persona por WhatsApp. Usala solo si el dato no esta en el contexto ni lo consigues por web; el paso quedara en espera de la respuesta y luego retomaras el llenado.");
            sb.AppendLine("Al preguntar por WhatsApp, usa tambien 'programar_reintento' (en_minutos) por si no responde: al cumplirse volveras a correr para enviarle un recordatorio; si responde antes, se cancela solo.");
        }
        if (canSendEmail)
        {
            sb.AppendLine("Ademas tienes 'enviar_correo' para ENVIAR un correo (avisar, pedir o confirmar algo). Redactas asunto y cuerpo. NO espera respuesta: no lo uses para CONSEGUIR un dato que necesites en este mismo paso.");
        }
        if (context.VoiceCallResult is { } vc)
        {
            // Reanudacion (ADR-0091): ya hay resultado de una llamada que pediste. Usalo para terminar de llenar.
            sb.AppendLine("YA tienes el resultado de la llamada que solicitaste (ver 'Resultado de la llamada' en el contexto): usalo para fijar los campos faltantes y envia el formulario. No vuelvas a llamar.");
        }
        if (context.WhatsAppReplyResult is not null)
        {
            // Reanudacion (ADR-0092): ya llego una respuesta de WhatsApp. Usarla; solo repreguntar si es imprescindible.
            sb.AppendLine("YA tienes la respuesta de WhatsApp (ver 'Respuesta por WhatsApp' en el contexto): usala para fijar los campos faltantes y envia el formulario. Solo vuelve a preguntar por WhatsApp si es imprescindible.");
        }
        sb.AppendLine("Reglas:");
        sb.AppendLine("- Llena SOLO con datos del contexto o que CONSIGAS con las herramientas (ej. buscar_web). NUNCA inventes datos.");
        sb.AppendLine("- Respeta los campos OBLIGATORIOS. En listas/opciones usa un valor valido de 'opciones'.");
        sb.AppendLine("- Un campo TABLA (tipo GridDetail) se fija con un ARREGLO de filas [{columna: valor}, ...] usando los ids de 'columnas'; una fila por item. No incluyas columnas calculadas (no estan en 'columnas'): el sistema las calcula solo.");
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
                // Para GridDetail (tabla de filas) NO se exponen las 'opciones' crudas: se decodifican en
                // 'columnas' (solo las que el agente debe CAPTURAR; las calculadas/rollup las computa el
                // servidor). El valor de un campo tabla es un ARREGLO de objetos {columna: valor} (ver 'valor').
                opciones = f.ControlType == FormControlType.GridDetail ? null : f.OptionsJson,
                columnas = f.ControlType == FormControlType.GridDetail ? GridColumnsSchema(f.OptionsJson) : null,
                valor = f.ControlType == FormControlType.GridDetail
                    ? "arreglo de filas: [{codigo_de_columna: valor, ...}, ...] (una entrada por columna capturable)"
                    : null
            })
        };
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Decodifica las columnas CAPTURABLES de un GridDetail para el esquema del agente: excluye las
    /// columnas calculadas/rollup (las recomputa el servidor al guardar) y las de gestion (subformularios por
    /// fila, fuera de alcance del llenado automatico). Para columnas de lista expone sus opciones validas.</summary>
    private static object[] GridColumnsSchema(string? optionsJson)
    {
        return FormGridCalculator.ParseColumns(optionsJson)
            .Where(c => string.IsNullOrWhiteSpace(c.Calc) && !c.IsGestion)
            .Select(c => (object)new
            {
                id = c.Id,
                etiqueta = c.Label,
                obligatoria = c.Required,
                opciones = c.IsSelect && c.Options is { Count: > 0 }
                    ? c.Options.Select(o => new { valor = o.Id, etiqueta = o.Label }).ToArray()
                    : null
            })
            .ToArray();
    }

    /// <summary>Aplica 'fijar_campos': acepta SOLO codigos que existen en el formulario (ignora los demas),
    /// convierte el valor a texto y los acumula. Devuelve al modelo que quedo fijado y que obligatorios faltan.</summary>
    private static string ApplySetFields(string argsJson, WorkflowAgentFormDto form, Dictionary<string, string?> fields)
    {
        var byCode = form.Fields.ToDictionary(f => f.FieldCode, StringComparer.Ordinal);
        var ignored = new List<string>();
        var gridErrors = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            if (!doc.RootElement.TryGetProperty("campos", out var campos) || campos.ValueKind != JsonValueKind.Object)
            {
                return """{"error": "'campos' debe ser un objeto {codigo: valor}"}""";
            }
            foreach (var p in campos.EnumerateObject())
            {
                if (!byCode.TryGetValue(p.Name, out var field)) { ignored.Add(p.Name); continue; }
                if (field.ControlType == FormControlType.GridDetail)
                {
                    // Campo tabla: el valor debe ser un ARREGLO de filas. Se serializa canonicamente (objetos de
                    // strings, solo columnas capturables) para guardarlo tal cual espera FormResponseService.
                    var (json, error) = BuildGridValue(p.Value, field.OptionsJson);
                    if (error is not null) { gridErrors.Add($"{p.Name}: {error}"); continue; }
                    fields[p.Name] = json;
                }
                else
                {
                    fields[p.Name] = ScalarToString(p.Value);
                }
            }
        }
        catch (JsonException)
        {
            return """{"error": "JSON invalido en 'campos'"}""";
        }

        var missing = form.Fields
            .Where(f => f.Required && (!fields.TryGetValue(f.FieldCode, out var v) || string.IsNullOrWhiteSpace(v)))
            .Select(f => f.FieldCode).ToList();
        var payload = new
        {
            ok = gridErrors.Count == 0,
            fijados = fields.Keys.ToList(),
            faltan_obligatorios = missing,
            ignorados_desconocidos = ignored,
            errores_tabla = gridErrors,
        };
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Convierte el valor de un campo GridDetail (arreglo de filas emitido por el agente) al string JSON
    /// canonico que guarda FormResponseService: un arreglo de objetos con SOLO las columnas capturables (se
    /// ignoran calculadas/rollup/gestion, que recomputa el servidor) y todos los valores como texto. Valida que
    /// cada fila tenga las columnas obligatorias. Devuelve (json, null) si ok, o (null, motivo) si no.</summary>
    private static (string? Json, string? Error) BuildGridValue(JsonElement value, string? optionsJson)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return (null, "el valor debe ser un arreglo de filas [{columna: valor}, ...]");
        }
        var columns = FormGridCalculator.ParseColumns(optionsJson)
            .Where(c => string.IsNullOrWhiteSpace(c.Calc) && !c.IsGestion)
            .ToList();
        if (columns.Count == 0)
        {
            return (null, "la tabla no tiene columnas capturables");
        }
        var capturable = columns.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var requiredCols = columns.Where(c => c.Required).Select(c => c.Id).ToList();

        var rows = new List<Dictionary<string, string>>();
        var rowIndex = 0;
        foreach (var rowEl in value.EnumerateArray())
        {
            rowIndex++;
            if (rowEl.ValueKind != JsonValueKind.Object)
            {
                return (null, $"la fila {rowIndex} no es un objeto {{columna: valor}}");
            }
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var col in rowEl.EnumerateObject())
            {
                if (!capturable.Contains(col.Name)) { continue; }
                var s = ScalarToString(col.Value);
                if (s is not null) { row[col.Name] = s; }
            }
            var missingCols = requiredCols
                .Where(id => !row.TryGetValue(id, out var v) || string.IsNullOrWhiteSpace(v))
                .ToList();
            if (missingCols.Count > 0)
            {
                return (null, $"la fila {rowIndex} no tiene las columnas obligatorias: {string.Join(", ", missingCols)}");
            }
            rows.Add(row);
        }
        return (JsonSerializer.Serialize(rows), null);
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

    /// <summary>ADR-0091: ejecuta 'buscar_web' -> navega con el cliente Colmena del nodo y devuelve el contenido
    /// legible al modelo. El invoker NO escribe BD; solo trae el dato. Un fallo (offline/timeout) vuelve como
    /// {ok:false,error} y el agente lo trata como "no lo consegui".</summary>
    private async Task<string> ExecuteWebSearchAsync(
        string argsJson, WorkflowAgentAssignmentDto assignment, Guid tenantId, CancellationToken cancellationToken)
    {
        if (assignment.ColmenaClientId is not Guid clientId)
        {
            return """{"ok": false, "error": "este paso no tiene un cliente Colmena configurado"}""";
        }

        string? url, selector;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            var root = doc.RootElement;
            url = root.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            selector = root.TryGetProperty("selector", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        }
        catch (JsonException)
        {
            return """{"ok": false, "error": "argumentos JSON invalidos"}""";
        }
        if (string.IsNullOrWhiteSpace(url))
        {
            return """{"ok": false, "error": "falta 'url'"}""";
        }

        var res = await _browserFetch.FetchAsync(
            clientId, assignment.ColmenaSessionKey, url!, selector, tenantId, cancellationToken);
        return res.Ok
            ? JsonSerializer.Serialize(new { ok = true, contenido = res.Content })
            : JsonSerializer.Serialize(new { ok = false, error = res.Error });
    }

    /// <summary>ADR-0093: ejecuta 'enviar_correo' -> envia por IEmailSender (config del tenant). Sincrono, sin
    /// pausa. Valida destinatario/asunto/cuerpo; el cuerpo (texto plano del agente) se pasa a HTML seguro. Un
    /// fallo (sin config, SMTP caido) vuelve como {ok:false,error} y el agente lo trata como "no se envio".</summary>
    private async Task<string> ExecuteSendEmailAsync(string argsJson, CancellationToken cancellationToken)
    {
        string? to, subject, body;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            var root = doc.RootElement;
            to = root.TryGetProperty("destinatario", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            subject = root.TryGetProperty("asunto", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            body = root.TryGetProperty("cuerpo", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
        }
        catch (JsonException)
        {
            return """{"ok": false, "error": "argumentos JSON invalidos"}""";
        }
        to = to?.Trim();
        if (string.IsNullOrWhiteSpace(to) || !IsLikelyEmail(to))
        {
            return """{"ok": false, "error": "falta un 'destinatario' de correo valido"}""";
        }
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
        {
            return """{"ok": false, "error": "falta 'asunto' o 'cuerpo'"}""";
        }

        var html = PlainToHtml(body!);
        var sent = await _email.SendAsync(to!, Clip(subject, 300)!, html, cancellationToken);
        return sent.Ok
            ? JsonSerializer.Serialize(new { ok = true, mensaje = "correo enviado" })
            : JsonSerializer.Serialize(new { ok = false, error = sent.Error ?? "no se pudo enviar el correo" });
    }

    /// <summary>Parte B: lista los formularios del tenant (por titulo/codigo) y las impresiones (plantillas) del
    /// tenant (por nombre). Solo lectura; todo tenant-scoped por el filtro global.</summary>
    private async Task<string> ExecuteListFormsAsync(CancellationToken cancellationToken)
    {
        var forms = await _db.FormDefinitions.AsNoTracking()
            .Where(d => !d.IsArchived)
            .OrderBy(d => d.Title)
            .Select(d => new { d.Title, d.Code })
            .Take(100)
            .ToListAsync(cancellationToken);
        var templates = await _db.QuoteTemplates.AsNoTracking()
            .OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name)
            .Select(t => new { t.Name, t.IsDefault })
            .Take(50)
            .ToListAsync(cancellationToken);
        return JsonSerializer.Serialize(new
        {
            ok = true,
            formularios = forms.Select(f => new { nombre = f.Title, codigo = f.Code }),
            impresiones = templates.Select(t => new { nombre = t.Name, predeterminada = t.IsDefault })
        });
    }

    /// <summary>Parte B: envia al CLIENTE del caso la impresion (PDF) de un formulario de ESTE caso. Resuelve la
    /// respuesta del formulario ANCLADA a la tarea (Reference == numero o "numero-n", como NodeNotifyService), la
    /// renderiza con la plantilla elegida (o la predeterminada) y la manda por la linea del nodo al telefono del
    /// cliente de la tarea (destinatario FIJO: no se puede elegir otro numero). La autorizacion de que se puede
    /// enviar la gobierna el PROMPT del nodo.</summary>
    private async Task<string> ExecuteSendImpresionAsync(
        string argsJson, WorkflowAgentContextDto context, CancellationToken cancellationToken)
    {
        if (context.Assignment?.WhatsAppLineId is not Guid lineId)
        {
            return """{"ok": false, "error": "este paso no tiene una linea de WhatsApp para enviar"}""";
        }
        if (context.Task is not { } task)
        {
            return """{"ok": false, "error": "no hay una tarea en este caso para resolver el documento"}""";
        }
        var phone = NullIfBlank(task.RequesterPhone) ?? NullIfBlank(task.Tercero?.Telefono);
        if (phone is null)
        {
            return """{"ok": false, "error": "no hay telefono del cliente en la tarea para enviar"}""";
        }

        string? formName, templateName, caption;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            var root = doc.RootElement;
            formName = ArgStr(root, "formulario");
            templateName = ArgStr(root, "impresion");
            caption = ArgStr(root, "mensaje");
        }
        catch (JsonException) { return """{"ok": false, "error": "argumentos JSON invalidos"}"""; }

        if (string.IsNullOrWhiteSpace(formName))
        {
            return """{"ok": false, "error": "falta 'formulario' (nombre o codigo)"}""";
        }

        var fn = formName.Trim().ToLower();
        var num = task.Number;

        // Definiciones (no archivadas) cuyo titulo o codigo normalice al nombre pedido. Puede haber homonimas
        // (ej. "Cotizacion" FRM-CRM-COT del CRM y "COTIZACION" COT del flujo); el desempate es por la respuesta.
        var matchIds = await _db.FormDefinitions.AsNoTracking()
            .Where(d => !d.IsArchived && (d.Title.ToLower() == fn || d.Code.ToLower() == fn))
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        Guid? templateId = null;
        if (!string.IsNullOrWhiteSpace(templateName))
        {
            var tn = templateName.Trim().ToLower();
            var tplId = await _db.QuoteTemplates.AsNoTracking()
                .Where(t => t.Name.ToLower() == tn).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);
            if (tplId is null)
            {
                return JsonSerializer.Serialize(new { ok = false, error = $"no existe una impresion '{templateName}'" });
            }
            templateId = tplId;
        }

        // Documentos (respuestas de formulario diligenciadas) anclados a ESTE caso (Reference == num o "num-n").
        var caseDocs = await (
            from r in _db.FormResponses.AsNoTracking()
            where r.Reference != null && (r.Reference == num || r.Reference.StartsWith(num + "-"))
            join d in _db.FormDefinitions.AsNoTracking() on r.DefinitionId equals d.Id
            where !d.IsArchived
            select new { ResponseId = r.Id, DefId = d.Id, d.Title, d.Code, r.IsActive, r.CreatedAt }
        ).ToListAsync(cancellationToken);
        if (caseDocs.Count == 0)
        {
            return JsonSerializer.Serialize(new { ok = false, error = $"este caso ({num}) no tiene ningun documento (formulario diligenciado) para enviar." });
        }

        // Eleccion: PRIMERO el documento cuya definicion calzo con el nombre pedido (la activa mas reciente). Si el
        // nombre NO calzo con ninguna definicion del caso -el agente a veces pasa la REFERENCIA del caso (ej
        // "T00090-1") o un nombre aproximado-, se cae en CUALQUIER documento del caso, PREFIRIENDO una cotizacion.
        // Asi un argumento equivocado del agente no deja al cliente sin su documento.
        var chosen =
            (matchIds.Count > 0
                ? caseDocs.Where(c => matchIds.Contains(c.DefId))
                    .OrderByDescending(c => c.IsActive).ThenBy(c => c.CreatedAt).FirstOrDefault()
                : null)
            ?? caseDocs
                .OrderByDescending(c => ((c.Title ?? "") + " " + (c.Code ?? "")).ToLower().Contains("cotiz"))
                .ThenByDescending(c => c.IsActive).ThenBy(c => c.CreatedAt).First();
        var rid = chosen.ResponseId;
        var formTitle = chosen.Title;

        Forms.QuoteDocument? pdf;
        try { pdf = await _quoteDoc.RenderResponsePdfAsync(rid, templateId, cancellationToken); }
        catch (Exception ex) { return JsonSerializer.Serialize(new { ok = false, error = $"no se pudo generar el PDF: {ex.Message}" }); }
        if (pdf is null)
        {
            return JsonSerializer.Serialize(new { ok = false, error = "no se pudo generar el PDF (plantilla vacia o render fallido)" });
        }

        var b64 = Convert.ToBase64String(pdf.Bytes);
        bool sent;
        try
        {
            sent = await _sender.SendWhatsAppDocumentAsync(
                lineId, phone, b64, pdf.MimeType, pdf.FileName, NullIfBlank(caption), Guid.Empty, cancellationToken);
        }
        catch (Exception ex) { return JsonSerializer.Serialize(new { ok = false, error = $"error enviando el documento: {ex.Message}" }); }
        if (!sent)
        {
            return JsonSerializer.Serialize(new { ok = false, error = "no se pudo enviar el documento por esta linea (el proveedor puede no soportar envio directo de archivo; se requeriria una plantilla con encabezado de documento)" });
        }

        // Traza (#2): el conector solo ENVIA; aqui dejamos el documento como SALIENTE en el hilo del cliente para
        // que se vea en la conversacion y en la bitacora (antes el PDF salia por el proveedor pero no quedaba
        // ningun mensaje registrado). Best-effort: registrar la traza NUNCA invalida un envio ya realizado.
        try
        {
            var conv = await _db.Conversations
                .FirstOrDefaultAsync(c => c.WhatsAppLineId == lineId && c.ContactPhone == phone, cancellationToken);
            var sentAt = DateTimeOffset.UtcNow;
            if (conv is null)
            {
                var tenantId = await _db.WhatsAppLines.AsNoTracking()
                    .Where(l => l.Id == lineId).Select(l => l.TenantId).FirstOrDefaultAsync(cancellationToken);
                conv = new Ecorex.Domain.Entities.Conversation
                {
                    TenantId = tenantId,
                    ContactPhone = phone,
                    WhatsAppLineId = lineId,
                    LastMessageAt = sentAt
                };
                _db.Conversations.Add(conv);
            }
            else
            {
                conv.LastMessageAt = sentAt;
            }
            _db.Messages.Add(new Ecorex.Domain.Entities.Message
            {
                TenantId = conv.TenantId,
                ConversationId = conv.Id,
                Direction = Ecorex.Domain.Enums.MessageDirection.Outbound,
                Body = NullIfBlank(caption) ?? $"Documento enviado: {pdf.FileName}",
                MessageType = "document",
                MediaType = Ecorex.Domain.Enums.MessageMediaType.Document,
                MediaMimeType = pdf.MimeType,
                MediaFileName = pdf.FileName,
                SentByName = "Agente de IA",
                SentAt = sentAt
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch { /* best-effort: la traza no debe tumbar un envio ya hecho */ }

        return JsonSerializer.Serialize(new { ok = true, mensaje = $"impresion de '{formTitle}' enviada al cliente" });
    }

    private static string? ArgStr(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s!.Trim();

    /// <summary>Validacion minima de email (hay un '@' con algo antes y un dominio con punto despues).</summary>
    private static bool IsLikelyEmail(string value)
    {
        var at = value.IndexOf('@');
        if (at <= 0 || at >= value.Length - 3) { return false; }
        var domain = value[(at + 1)..];
        return domain.Contains('.') && !value.Contains(' ');
    }

    /// <summary>Convierte el texto plano del agente en HTML seguro: escapa y respeta los saltos de linea. El
    /// sender siempre manda HTML (IsBodyHtml=true), asi que sin esto los saltos se perderian y el texto podria
    /// romper el markup.</summary>
    private static string PlainToHtml(string text)
    {
        var escaped = System.Net.WebUtility.HtmlEncode(text.Trim());
        var withBreaks = escaped.Replace("\r\n", "\n").Replace("\n", "<br>");
        return $"<p>{withBreaks}</p>";
    }

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) { return null; }
        var t = value.Trim();
        return t.Length <= max ? t : t[..max];
    }

    /// <summary>Capa 2 (ADR-0091): describe en una linea legible lo que el agente hizo en una ronda, para
    /// mostrarlo como su "pensamiento" en vivo. Prefiere el texto propio del modelo (su narracion) si lo hay;
    /// si no, deriva una etiqueta de las herramientas que invoco. Nunca inventa: refleja lo que de verdad paso.</summary>
    /// <summary>True si el texto AFIRMA (en pasado/hecho) que ya se reenvio/adjunto la cotizacion u otro documento.
    /// Insensible a acentos. Solo detecta afirmaciones de envio COMPLETADO (no ofertas tipo "quieres que te lo
    /// reenvie?"), para no forzar un envio que el cliente no pidio.</summary>
    private static bool ClaimsDocumentSendWithoutTool(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return false; }
        var t = StripAccentsLower(text!);
        string[] claims =
        {
            "acabo de reenvi", "acabo de enviar", "acabo de mandar", "acabo de compartir",
            "te reenvie", "te reenvio ", "te he reenviado", "te lo reenvie", "ya te reenvie",
            "te he enviado", "te lo envie", "te la envie", "ya te envie", "ya te lo envie",
            "te acabo de", "enviado la cotiz", "enviada la cotiz", "adjunto la cotiz", "adjunto el documento",
            "te mande la cotiz", "te comparto la cotiz"
        };
        foreach (var c in claims) { if (t.Contains(c)) { return true; } }
        return false;
    }

    /// <summary>True si el cliente PIDE que le reenvien la cotizacion/documento (lo perdio, lo borro, no le llego,
    /// "mandamela de nuevo"). Insensible a acentos. Sirve para forzar 'enviar_impresion' cuando el agente responderia
    /// solo "confirmame si la recibiste" en vez de reenviar de verdad.</summary>
    private static bool AsksToResendDocument(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return false; }
        var t = StripAccentsLower(text!);
        // Debe referirse a un documento/cotizacion o a reenviarlo; evita falsos positivos de un "no me llego" suelto.
        var mentionsDoc = t.Contains("cotiz") || t.Contains("documento") || t.Contains("pdf")
            || t.Contains("archivo") || t.Contains("propuesta") || t.Contains("la cotizacion");
        string[] resendAsks =
        {
            "reenvia", "reenviame", "reenviar", "me la mandas de nuevo", "me lo mandas de nuevo",
            "mandamela de nuevo", "mandamelo de nuevo", "enviamela de nuevo", "enviamelo de nuevo",
            "mandamela", "mandamelo", "enviamela", "enviamelo", "pasamela", "pasamelo",
            "compartemela", "de nuevo la cotiz", "otra vez la cotiz", "la perdi", "lo perdi",
            "la borre", "lo borre", "no me llego", "no me ha llegado", "no la recibi", "no lo recibi",
            "no me la enviaste", "no me lo enviaste", "nunca me llego", "me la vuelves a", "me lo vuelves a"
        };
        var asksResend = false;
        foreach (var a in resendAsks) { if (t.Contains(a)) { asksResend = true; break; } }
        if (!asksResend) { return false; }
        // Un "la perdi / la borre / no me llego" ya implica el documento del caso; los verbos de reenvio generales
        // (mandamela, reenvia) exigen que se mencione un documento para no disparar con cualquier "mandame X".
        var impliesDoc = t.Contains("la perdi") || t.Contains("lo perdi") || t.Contains("la borre")
            || t.Contains("lo borre") || t.Contains("no me llego") || t.Contains("no la recibi")
            || t.Contains("no lo recibi") || t.Contains("de nuevo la cotiz") || t.Contains("otra vez la cotiz");
        return mentionsDoc || impliesDoc;
    }

    /// <summary>Minusculas sin acentos (para comparar texto del modelo de forma robusta).</summary>
    private static string StripAccentsLower(string s)
    {
        var norm = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var ch in norm)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private static string DescribeRound(string? modelText, IEnumerable<string> toolNames)
    {
        var names = toolNames.ToList();
        var webCount = names.Count(n => n == "buscar_web");
        string action =
            names.Contains("enviar_formulario") ? "Cerrando el paso..."
            : names.Contains("fijar_campos") ? "Diligenciando el formulario..."
            : names.Contains("enviar_impresion") ? "Enviando el documento al cliente..."
            : names.Contains("listar_formularios") ? "Buscando que documento reenviar..."
            : webCount > 1 ? $"Consultando {webCount} paginas en la web..."
            : webCount == 1 ? "Consultando la web..."
            : names.Contains("ver_formulario") ? "Revisando el formulario..."
            : names.Contains("llamar_telefono") ? "Pidiendo una llamada..."
            : names.Contains("preguntar_whatsapp") ? "Preguntando por WhatsApp..."
            : names.Contains("enviar_correo") ? "Enviando un correo..."
            : "Pensando...";
        // El texto del modelo (si viene) es su propia narracion: se muestra tal cual, acotado. Si no, la accion.
        var text = Clip(modelText, 160);
        return string.IsNullOrWhiteSpace(text) ? action : text!;
    }

    /// <summary>Describe en una linea el RESULTADO de 'enviar_impresion' para la bitacora del paso: si el
    /// documento se envio al cliente o, si no, el motivo exacto. Asi un "no llego el documento" deja de ser
    /// invisible: queda claro si el agente lo envio de verdad o solo lo prometio.</summary>
    private static string DescribeImpresionOutcome(string resultJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(resultJson) ? "{}" : resultJson);
            var root = doc.RootElement;
            var ok = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            if (ok) { return "Documento enviado al cliente."; }
            var error = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            return string.IsNullOrWhiteSpace(error) ? "No pudo enviar el documento." : $"No pudo enviar el documento: {Clip(error, 140)}";
        }
        catch (JsonException) { return "No pudo enviar el documento."; }
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
