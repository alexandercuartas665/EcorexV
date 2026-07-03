using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class AiInferenceService : IAiInferenceService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IAiProviderClient _client;
    private readonly IAiUsageService _usage;
    private readonly IAiAgentCacheService _cache;
    private readonly IReadOnlyList<IAgentToolset> _toolsets;
    private readonly TimeProvider _clock;

    // Nombres de TODAS las herramientas (de todos los toolsets). Se usan para sanear la respuesta saliente:
    // a veces el modelo escribe la llamada como texto (p.ej. "crear_lead(...)") y eso NO debe llegar al cliente.
    private readonly IReadOnlyCollection<string> _allToolNames;

    // Tope de vueltas del bucle de herramientas: evita ciclos infinitos si el modelo insiste en llamar tools.
    private const int MaxToolRounds = 6;

    // Zona horaria del tenant demo (America/Bogota = UTC-5, sin horario de verano). Mientras el Tenant no
    // guarde su propia zona, anclamos aqui para que el agente calcule fechas relativas con el anio correcto.
    private static readonly TimeSpan TenantOffset = TimeSpan.FromHours(-5);

    public AiInferenceService(IApplicationDbContext db, ISecretProtector secretProtector, IAiProviderClient client, IAiUsageService usage, IAiAgentCacheService cache, IEnumerable<IAgentToolset> toolsets, TimeProvider clock)
    {
        _db = db;
        _secretProtector = secretProtector;
        _client = client;
        _usage = usage;
        _cache = cache;
        _toolsets = toolsets.ToList();
        _clock = clock;
        _allToolNames = _toolsets.SelectMany(t => t.GetSpecs()).Select(s => s.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // Herramientas que el agente tiene DESHABILITADAS (AiAgent.DisabledToolsJson). Vacio = todas habilitadas.
    private static HashSet<string> ParseDisabledTools(string? json)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) { return set; }
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            if (list is not null) { foreach (var t in list) { if (!string.IsNullOrWhiteSpace(t)) { set.Add(t.Trim()); } } }
        }
        catch { /* json invalido: no deshabilitamos nada */ }
        return set;
    }

    // Chat de prueba: la sesion de cache es el AgentId y el operador prueba con reservas reales (autonomo).
    public Task<AiChatResult> TestChatAsync(Guid agentId, IReadOnlyList<AiChatTurn> turns, string? systemPromptOverride = null, Guid? actorUserId = null, string? imageBase64 = null, string? imageMime = null, CancellationToken cancellationToken = default)
        => RunCoreAsync(agentId, agentId, turns, systemPromptOverride, autonomous: true, actorUserId ?? Guid.Empty, conversationId: null, imageBase64, imageMime, cancellationToken);

    // Atencion real por una linea: la sesion de cache es la conversacion (linea+contacto) y la autonomia
    // (ejecutar acciones de verdad vs solo sugerir) la fija el binding de la linea.
    public Task<AiChatResult> RespondAsync(Guid agentId, Guid sessionId, IReadOnlyList<AiChatTurn> turns, bool autonomous, Guid actorUserId, CancellationToken cancellationToken = default)
        => RunCoreAsync(agentId, sessionId, turns, null, autonomous, actorUserId, conversationId: sessionId, imageBase64: null, imageMime: null, cancellationToken);

    private async Task<AiChatResult> RunCoreAsync(Guid agentId, Guid sessionId, IReadOnlyList<AiChatTurn> turns, string? systemPromptOverride, bool autonomous, Guid actorUserId, Guid? conversationId, string? imageBase64, string? imageMime, CancellationToken cancellationToken)
    {
        var agent = await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null) { return new AiChatResult(false, null, "El agente no existe."); }

        // La cuenta del proveedor (API key, modelo, base url) la define el Super Admin (config global).
        var providerCfg = await _db.AiProviderConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Provider == agent.Provider, cancellationToken);
        if (providerCfg is null || !providerCfg.IsEnabled || string.IsNullOrWhiteSpace(providerCfg.ApiKeyEncrypted))
        {
            return new AiChatResult(false, null, $"El proveedor {agent.Provider} no esta habilitado en la plataforma.");
        }

        string apiKey;
        try { apiKey = _secretProtector.Unprotect(providerCfg.ApiKeyEncrypted); }
        catch { return new AiChatResult(false, null, "La API key del proveedor esta cifrada con una version anterior. Vuelve a guardarla en Servidores de IA."); }

        var meta = AiProviderCatalog.For(agent.Provider);
        var model = !string.IsNullOrWhiteSpace(agent.Model) ? agent.Model!
            : !string.IsNullOrWhiteSpace(providerCfg.Model) ? providerCfg.Model!
            : meta.DefaultModel;

        if (turns.Count == 0) { return new AiChatResult(false, null, "Escribe un mensaje para probar el agente."); }

        // Control de cupo: si el plan tiene limite duro y ya se agoto el mes, no se ejecuta.
        // (Las consultas a BD se hacen en serie sobre el DbContext scoped: cupo -> prompt -> proveedor.)
        var quota = await _usage.GetQuotaAsync(cancellationToken);
        if (quota.Exceeded && quota.Hard)
        {
            return new AiChatResult(false, null, $"Alcanzaste el limite de tokens de IA de tu plan este mes ({quota.MonthlyLimitTokens:N0}). Actualiza tu plan para seguir usando los agentes.");
        }

        // Recursos del agente (todos los tipos): se usan para componer el prompt y para resolver adjuntos.
        var resources = await _db.AiAgentResources.AsNoTracking()
            .Where(r => r.AgentId == agentId)
            .OrderBy(r => r.SortOrder)
            .Select(r => new AiChatAttachment(r.Name, r.ResourceType, r.FileUrl, r.FileName, r.Detail))
            .ToListAsync(cancellationToken);

        // Cargamos los campos cache y los valores capturados de la sesion para inyectarlos al prompt.
        var cacheFields = await _db.AiAgentCacheFields.AsNoTracking()
            .Where(f => f.AgentId == agentId)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Label)
            .Select(f => new CacheFieldInfo(f.FieldKey, f.Label, f.Description, f.IsUpdatable))
            .ToListAsync(cancellationToken);

        var cacheValues = await _db.AiAgentCacheValues.AsNoTracking()
            .Where(v => v.AgentId == agentId && v.SessionId == sessionId)
            .ToDictionaryAsync(v => v.FieldKey, v => v.Value, cancellationToken);

        var systemPrompt = await BuildSystemPrompt(agentId, systemPromptOverride ?? agent.SystemPrompt, resources, cacheFields, cacheValues, turns, autonomous, cancellationToken);

        // Log de prompts: registramos cada llamada al LLM con su titulo y fecha/hora.
        var debugPrompts = new List<AiDebugPrompt>
        {
            new("Prompt principal del agente (enrutador + recursos + estado de cache)", DateTimeOffset.UtcNow, systemPrompt)
        };

        // Bucle de herramientas (function calling): el agente puede consultar y registrar datos in-process.
        // Si el modelo no llama ninguna herramienta, equivale a una respuesta normal.
        var actor = actorUserId;
        var disabledTools = ParseDisabledTools(agent.DisabledToolsJson);
        // Contexto ambiental para herramientas de vision: conversacion en curso y/o imagen pendiente
        // (sandbox/emulador). Fluye por el await hasta ExecuteAsync de los toolsets.
        using var _toolCtx = AiToolRunContext.Begin(conversationId, imageBase64, imageMime);
        var (result, sessionCompleted) = await RunToolLoopAsync(
            agent.Provider, apiKey, providerCfg.BaseUrl, model, systemPrompt, turns, autonomous, actor, disabledTools, debugPrompts, cancellationToken);

        // Todo consumo de IA del tenant pasa por el modulo de tokens (incluido el chat de prueba).
        if (result.Ok)
        {
            await _usage.RecordAsync(agent.Id, agent.Provider, model, result.InputTokens, result.OutputTokens, "test", true, cancellationToken);
        }

        // Despues de la respuesta principal, hacemos una segunda llamada (mas barata) que infiera los
        // datos cache a partir del ultimo turno cliente+agente. Cada campo extraido se persiste via el
        // servicio (que respeta IsUpdatable, asi que los sticky no se sobrescriben).
        if (result.Ok && cacheFields.Count > 0 && !string.IsNullOrWhiteSpace(result.Text))
        {
            try
            {
                await ExtractAndStoreCacheUpdatesAsync(
                    agentId, sessionId, agent.Provider, apiKey, providerCfg.BaseUrl, model,
                    cacheFields, cacheValues, turns, result.Text!, resources, debugPrompts, cancellationToken);
            }
            catch
            {
                // La extraccion no debe romper la respuesta al cliente: si falla, seguimos sin actualizar la cache.
            }
        }

        // Cierre del proceso: si en esta vuelta se concreto un cierre, vaciamos la cache de la sesion para
        // dejar al agente listo para atender a un nuevo cliente desde cero.
        if (sessionCompleted)
        {
            try { await _cache.ClearValuesAsync(agentId, sessionId, actor, cancellationToken); }
            catch { /* limpiar la cache no debe romper la respuesta */ }
        }

        // Entrega de recursos: el modelo marca [[enviar: Nombre]] y aqui adjuntamos el recurso (archivo o texto).
        if (result.Ok && !string.IsNullOrEmpty(result.Text))
        {
            var (cleanText, attachments) = ExtractAttachments(result.Text!, resources);
            cleanText = StripToolCallArtifacts(cleanText, _allToolNames);
            return result with { Text = cleanText, Attachments = attachments, DebugPrompts = debugPrompts };
        }

        return result with { DebugPrompts = debugPrompts };
    }

    // Linea de contexto temporal que se inyecta al inicio del prompt: el modelo la usa para resolver
    // "hoy", "manana", "el viernes" y para el parametro fecha (AAAA-MM-DD) de las herramientas.
    private string BuildDateContextLine()
    {
        var now = _clock.GetUtcNow().ToOffset(TenantOffset);
        var dia = SpanishDay(now.DayOfWeek);
        return $"FECHA Y HORA ACTUAL: hoy es {dia} {now:yyyy-MM-dd}, {now:HH:mm} (zona America/Bogota). " +
               "Usa SIEMPRE esta fecha como referencia para calcular dias relativos (hoy, manana, el viernes, etc.) y para el " +
               "parametro fecha (AAAA-MM-DD) de las herramientas. NUNCA uses un anio distinto al actual.";
    }

    private static string SpanishDay(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "lunes",
        DayOfWeek.Tuesday => "martes",
        DayOfWeek.Wednesday => "miercoles",
        DayOfWeek.Thursday => "jueves",
        DayOfWeek.Friday => "viernes",
        DayOfWeek.Saturday => "sabado",
        _ => "domingo"
    };

    /// <summary>
    /// Ejecuta la conversacion con function calling: pasa los turnos + las herramientas de agenda al
    /// proveedor; mientras el modelo pida herramientas, las ejecuta IN-PROCESS (misma ruta de reserva que
    /// la recepcion, con anti-overbooking) y vuelve a llamar con los resultados, hasta que el modelo da su
    /// respuesta final en texto. Devuelve el texto final y si se concreto una reserva en el camino.
    /// </summary>
    private async Task<(AiChatResult Result, bool SessionCompleted)> RunToolLoopAsync(
        AiProvider provider, string apiKey, string? baseUrl, string model, string systemPrompt,
        IReadOnlyList<AiChatTurn> turns, bool autonomous, Guid actorUserId, ISet<string> disabledTools, List<AiDebugPrompt> debugPrompts, CancellationToken ct)
    {
        // Agregamos las herramientas de TODOS los toolsets registrados, omitiendo las que el agente
        // tiene deshabilitadas. Mapeamos cada nombre de herramienta a su toolset para el despacho.
        var specs = new List<AiToolSpec>();
        var ownerByTool = new Dictionary<string, IAgentToolset>(StringComparer.OrdinalIgnoreCase);
        foreach (var ts in _toolsets)
        {
            foreach (var spec in ts.GetSpecs())
            {
                if (disabledTools.Contains(spec.Name) || ownerByTool.ContainsKey(spec.Name)) { continue; }
                ownerByTool[spec.Name] = ts;
                specs.Add(spec);
            }
        }

        // Historial inicial: los turnos del chat como mensajes de herramienta.
        var messages = new List<AiToolMessage>();
        foreach (var t in turns)
        {
            var role = string.Equals(t.Role, "model", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
            messages.Add(new AiToolMessage(role, t.Text));
        }

        var totalIn = 0;
        var totalOut = 0;
        var sessionCompleted = false;
        string? lastText = null;
        // Red de seguridad de CIERRE: el modelo a veces afirma "ya te registre / un asesor te contactara"
        // sin invocar la herramienta de cierre, dejando al cliente FUERA del pipeline. Si detectamos ese
        // caso lo forzamos a invocarla una sola vez.
        var closeToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "crear_lead" };
        var closeToolInvoked = false;
        var closeNudged = false;

        for (var round = 1; round <= MaxToolRounds; round++)
        {
            var completion = await _client.CompleteWithToolsAsync(provider, apiKey, baseUrl, model, systemPrompt, messages, specs, ct);
            totalIn += completion.InputTokens;
            totalOut += completion.OutputTokens;

            if (!completion.Ok)
            {
                return (new AiChatResult(false, null, completion.Error, totalIn, totalOut), sessionCompleted);
            }

            // Sin herramientas pedidas: el modelo entrega su respuesta final.
            if (completion.ToolCalls.Count == 0)
            {
                // Si afirma un cierre (registro/inscripcion/que un asesor lo contactara) pero NUNCA invoco
                // una herramienta de cierre, lo forzamos una sola vez: no podemos prometerle al cliente que
                // quedo en el pipeline si no creamos el lead/inscripcion.
                if (!closeToolInvoked && !closeNudged && ClaimsCloseWithoutTool(completion.Text))
                {
                    closeNudged = true;
                    messages.Add(new AiToolMessage("assistant", completion.Text));
                    messages.Add(new AiToolMessage("user",
                        "[SISTEMA] Afirmaste que registraste/inscribiste al cliente o que un asesor lo contactara, " +
                        "pero NO invocaste ninguna herramienta de cierre (crear_lead). " +
                        "Es OBLIGATORIO: invoca AHORA la herramienta de cierre adecuada con los datos que ya tienes " +
                        "(nombre y canal/tipo de cliente). Si te falta el nombre, pidelo en vez de afirmar el registro. " +
                        "No respondas solo con texto."));
                    debugPrompts.Add(new AiDebugPrompt(
                        "Red de seguridad de cierre",
                        DateTimeOffset.UtcNow,
                        "El modelo afirmo un cierre sin invocar la herramienta; se le pidio invocarla.",
                        completion.Text));
                    continue;
                }
                return (new AiChatResult(true, completion.Text ?? lastText, null, totalIn, totalOut), sessionCompleted);
            }

            // El modelo pidio una o mas herramientas: las registramos en el log y agregamos el turno assistant.
            lastText = completion.Text;
            var callsLog = new StringBuilder();
            foreach (var c in completion.ToolCalls) { callsLog.AppendLine($"-> {c.Name}({c.ArgumentsJson})"); }
            debugPrompts.Add(new AiDebugPrompt(
                $"IA solicito herramientas (ronda {round})",
                DateTimeOffset.UtcNow,
                (string.IsNullOrWhiteSpace(completion.Text) ? "" : completion.Text + "\n\n") + callsLog.ToString().TrimEnd()));

            messages.Add(new AiToolMessage("assistant", completion.Text, completion.ToolCalls));

            // Ejecuta cada herramienta in-process y agrega su resultado como mensaje "tool".
            foreach (var call in completion.ToolCalls)
            {
                if (closeToolNames.Contains(call.Name)) { closeToolInvoked = true; }
                var owner = ownerByTool.GetValueOrDefault(call.Name);
                var exec = owner is null
                    ? new AgentToolResult(JsonSerializer.Serialize(new { ok = false, error = $"Herramienta no disponible o deshabilitada: {call.Name}" }), false)
                    : await owner.ExecuteAsync(call.Name, call.ArgumentsJson, actorUserId, autonomous, ct);
                if (exec.SessionCompleted) { sessionCompleted = true; }

                debugPrompts.Add(new AiDebugPrompt(
                    $"Herramienta ejecutada: {call.Name}",
                    DateTimeOffset.UtcNow,
                    $"Argumentos:\n{call.ArgumentsJson}",
                    exec.Json));

                messages.Add(new AiToolMessage("tool", exec.Json, null, call.Id, call.Name));
            }
        }

        // Se agoto el tope de vueltas sin respuesta final: devolvemos el ultimo texto o un aviso.
        return (new AiChatResult(true, lastText ?? "Estoy procesando tu solicitud, dame un momento.", null, totalIn, totalOut), sessionCompleted);
    }

    // Detecta cuando el texto AFIRMA un cierre (registro / que un asesor lo contactara) para forzar
    // la herramienta de cierre si el modelo no la invoco. Insensible a acentos.
    private static bool ClaimsCloseWithoutTool(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return false; }
        var n = StripAccents(text.ToLowerInvariant());
        return n.Contains("registr")                 // he registrado / te registre / quedaste registrada
            || n.Contains("contactar")               // un asesor te contactara / se pondra en contacto
            || n.Contains("se pondra en contacto")
            || n.Contains("se comunicar")
            || n.Contains("inscr")                   // quedaste inscrito / inscripcion
            || n.Contains("tu solicitud")
            || n.Contains("tu pedido");
    }

    private static string StripAccents(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    // Arma el prompt del sistema: prompt base + enrutador + catalogo de recursos + ESTADO DE CACHE
    // (solo lo capturado) + ultimos 5 eventos del chat.
    private async Task<string> BuildSystemPrompt(
        Guid agentId,
        string basePrompt,
        IReadOnlyList<AiChatAttachment> resources,
        IReadOnlyList<CacheFieldInfo> cacheFields,
        IReadOnlyDictionary<string, string?> cacheValues,
        IReadOnlyList<AiChatTurn> turns,
        bool autonomous,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        // Ancla temporal: el modelo no conoce "hoy"; sin esto reserva con anios equivocados.
        sb.AppendLine(BuildDateContextLine());
        sb.AppendLine();
        sb.AppendLine("REGLA CRITICA DE SALIDA: usa las herramientas SOLO por el mecanismo de funciones, NUNCA las escribas como texto. " +
            "El cliente JAMAS debe ver nombres de funciones, llamadas tipo crear_lead(...), JSON interno, marcadores ni razonamiento. " +
            "Tu respuesta es unicamente el mensaje natural para el cliente.");
        // Modo de operacion de la linea: en sugerencia el agente NO finaliza solo, deja la solicitud al asesor.
        if (!autonomous)
        {
            sb.AppendLine();
            sb.AppendLine("MODO SUGERENCIA: no puedes confirmar acciones por ti mismo. Cuando el cliente acepte, usa la herramienta correspondiente para REGISTRAR la solicitud (quedara PENDIENTE de que un asesor la confirme) e informa con calidez que un asesor confirmara en breve. No afirmes que la solicitud ya quedo confirmada.");
        }
        sb.AppendLine();
        sb.Append(ExpandResourceRefs(basePrompt, resources));

        var prompts = await _db.AiAgentPrompts.AsNoTracking()
            .Where(p => p.AgentId == agentId)
            .OrderBy(p => p.SortOrder)
            .Select(p => new { p.Name, p.Rule, p.Body })
            .ToListAsync(ct);
        if (prompts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Enrutador de prompts: evalua el mensaje del cliente y, si coincide alguna de estas reglas, sigue PRIMERO las instrucciones del prompt correspondiente (ademas del comportamiento base). Si ninguna aplica, responde con el comportamiento base.");
            foreach (var p in prompts)
            {
                sb.AppendLine();
                sb.AppendLine($"### Prompt \"{p.Name}\"");
                sb.AppendLine($"Regla (cuando usarlo): {(string.IsNullOrWhiteSpace(p.Rule) ? "(sin regla; usar a criterio)" : p.Rule)}");
                sb.AppendLine($"Instrucciones: {ExpandResourceRefs(p.Body, resources)}");
            }
        }

        if (resources.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Recursos disponibles. REGLA IMPORTANTE: cuando vayas a comunicar el contenido de un recurso (precios, politicas, textos, imagenes, videos, PDF, ubicacion), NO lo reescribas ni lo resumas: entregalo EXACTO incluyendo en tu respuesta el marcador [[enviar: Nombre exacto del recurso]]. El sistema agregara el contenido o el archivo tal cual. Puedes acompanarlo con una frase breve, pero el contenido del recurso lo entrega el marcador.");
            foreach (var r in resources)
            {
                var kind = r.ResourceType == AgentResourceType.Text ? "Texto" : r.ResourceType.ToString();
                var desc = string.IsNullOrWhiteSpace(r.Detail) ? "archivo" : r.Detail;
                sb.AppendLine($"- ({kind}) {r.Name}: {desc}  -> entregar con [[enviar: {r.Name}]]");
            }
        }

        // Datos del cliente que el sistema ya capturo (los listamos solo si tienen valor; las definiciones
        // y la tarea de buscar datos faltantes son responsabilidad del agente de cache, no del agente
        // principal). Asi el agente sabe en que momento de la conversacion esta sin abrumarlo con todos
        // los campos pendientes.
        var captured = cacheFields
            .Where(f => cacheValues.TryGetValue(f.FieldKey, out var v) && !string.IsNullOrWhiteSpace(v))
            .ToList();
        if (captured.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("### Datos que ya conocemos del cliente (estado de la cache)");
            sb.AppendLine("Estos datos ya estan capturados por el sistema. Usalos para decidir tu siguiente paso: NO le pidas al cliente algo que ya sabes, y avanza el guion si los datos que ya tienes cumplen lo que pide tu prompt enrutado.");
            foreach (var f in captured)
            {
                sb.AppendLine($"- {f.FieldKey}: {cacheValues[f.FieldKey]}");
            }
        }

        // Ultimos 5 eventos del chat — los proveedores ya reciben todo el historial via los turns,
        // pero el agente a veces se pierde en prompts largos. Esta seccion lo focaliza en lo mas reciente.
        if (turns.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("### Ultimos eventos del chat (lo mas reciente al final)");
            sb.AppendLine("Estos son los ultimos mensajes intercambiados. Usalos como contexto inmediato para tu siguiente respuesta. Recuerda que tu objetivo es avanzar el guion segun lo que el cliente diga, no repetir lo que ya enviaste.");
            var lastN = turns.Count > 5 ? turns.Skip(turns.Count - 5).ToList() : turns.ToList();
            foreach (var t in lastN)
            {
                var who = string.Equals(t.Role, "user", StringComparison.OrdinalIgnoreCase) ? "Cliente" : "Agente";
                sb.AppendLine($"- {who}: {BuildTurnLine(t.Text, t.Attachments)}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Segunda llamada al LLM (corta) que extrae datos del ultimo turno y los persiste en la cache de la sesion.
    /// El resultado del LLM debe ser un JSON plano {clave: valor}. Los campos sticky con valor previo NO se
    /// sobreescriben (el servicio de cache lo enforza). Los valores "PENDIENTE" se ignoran.
    /// </summary>
    private async Task ExtractAndStoreCacheUpdatesAsync(
        Guid agentId,
        Guid sessionId,
        AiProvider provider,
        string apiKey,
        string? baseUrl,
        string model,
        IReadOnlyList<CacheFieldInfo> fields,
        IReadOnlyDictionary<string, string?> currentValues,
        IReadOnlyList<AiChatTurn> originalTurns,
        string botResponse,
        IReadOnlyList<AiChatAttachment> resources,
        List<AiDebugPrompt> debugPrompts,
        CancellationToken ct)
    {
        var lastUser = originalTurns.LastOrDefault(t => string.Equals(t.Role, "user", StringComparison.OrdinalIgnoreCase))?.Text ?? "";

        var sysSb = new StringBuilder();
        sysSb.AppendLine("Eres un extractor de datos para una empresa. NO debes responder al cliente.");
        sysSb.AppendLine("Tu unico trabajo es leer la ultima interaccion cliente+agente y devolver un JSON plano con los campos que puedas inferir CON CERTEZA del mensaje del cliente.");
        sysSb.AppendLine("Reglas:");
        sysSb.AppendLine("- NO inventes datos. Si no esta claro, NO incluyas el campo.");
        sysSb.AppendLine("- Si un campo ya tiene valor y el cliente no lo cambia, NO lo incluyas (no es necesario reescribirlo).");
        sysSb.AppendLine("- NO incluyas el valor literal \"PENDIENTE\".");
        sysSb.AppendLine("- Responde UNICAMENTE el JSON, sin texto antes ni despues, sin markdown.");
        sysSb.AppendLine();
        sysSb.AppendLine("### Campos a capturar");
        foreach (var f in fields)
        {
            sysSb.AppendLine($"- {f.FieldKey}: {(string.IsNullOrWhiteSpace(f.Description) ? f.Label : f.Description)}");
        }
        sysSb.AppendLine();
        sysSb.AppendLine("### Estado actual de la cache");
        var anyKnown = false;
        foreach (var f in fields)
        {
            if (currentValues.TryGetValue(f.FieldKey, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                sysSb.AppendLine($"- {f.FieldKey} = {v}");
                anyKnown = true;
            }
        }
        if (!anyKnown) { sysSb.AppendLine("(vacio)"); }
        sysSb.AppendLine();
        sysSb.AppendLine("### Formato de respuesta");
        sysSb.AppendLine("JSON plano. Ejemplo: {\"tipo_cliente\":\"Interesado\",\"opcion_elegida\":\"Pronto\"}");
        sysSb.AppendLine("Si no hay nada nuevo, responde {}");

        // Transcripcion completa de la conversacion para que el extractor tenga TODO el contexto
        // (no solo el ultimo turno). Esto le permite inferir datos que se mencionaron en mensajes anteriores.
        // Importante: los turnos del agente pueden tener su texto vacio cuando solo entregaron un recurso
        // (ej. saludo_video2). Reconstruimos lo que realmente vio el cliente usando los Attachments del turno
        // y, para el turno actual, expandiendo los markers [[enviar: X]] del texto crudo del LLM.
        var transcript = new StringBuilder();
        transcript.AppendLine("### Transcripcion completa de la conversacion hasta ahora");
        transcript.AppendLine("Estos son TODOS los mensajes intercambiados, en orden cronologico. El cliente y el agente pueden haber compartido datos en cualquier turno previo, no solo en el ultimo. Cuando el agente envia un recurso, anotamos que recurso entrego y de que trata (asi puedes inferir el contexto incluso si no se ve el contenido literal del recurso).");
        transcript.AppendLine();
        foreach (var t in originalTurns)
        {
            var who = string.Equals(t.Role, "user", StringComparison.OrdinalIgnoreCase) ? "Cliente" : "Agente";
            transcript.AppendLine($"{who}: {BuildTurnLine(t.Text, t.Attachments)}");
            transcript.AppendLine();
        }
        // La respuesta del agente en este turno aun no esta en originalTurns; la agregamos al final.
        // Aqui usamos el texto crudo (con markers) y expandimos los markers usando el catalogo de recursos.
        transcript.AppendLine($"Agente (respuesta actual): {ExpandMarkersForTranscript(botResponse, resources)}");

        var userTurn = new AiChatTurn("user",
            transcript.ToString() + "\n\n¿Que campos puedes inferir del cliente a partir de TODA esta conversacion? Recuerda: solo agrega campos que puedes inferir CON CERTEZA y no incluyas los que ya tienen valor en el estado actual.");

        // Registramos el prompt del extractor en el log antes de la llamada (asi se ve aun si falla).
        // Luego rellenamos el Response cuando regrese el LLM.
        var extractorSystemPrompt = sysSb.ToString();
        var extractorEntry = new AiDebugPrompt(
            $"Agente de cache de datos (extractor; ultimo mensaje del cliente: \"{Truncate(lastUser, 60)}\")",
            DateTimeOffset.UtcNow,
            extractorSystemPrompt + "\n\n---\n[Turno del usuario al extractor]\n" + userTurn.Text);
        debugPrompts.Add(extractorEntry);
        var extractorIndex = debugPrompts.Count - 1;

        AiChatResult ext;
        try
        {
            ext = await _client.CompleteAsync(provider, apiKey, baseUrl, model, extractorSystemPrompt, new[] { userTurn }, ct);
        }
        catch (Exception callEx)
        {
            // Guardamos el error en la respuesta para que se vea en el log.
            debugPrompts[extractorIndex] = extractorEntry with { Response = $"[Llamada fallida] {callEx.GetType().Name}: {callEx.Message}" };
            return;
        }

        // Guardamos lo que respondio el LLM (texto crudo, sin parsear) para que se vea en el log de prompts.
        debugPrompts[extractorIndex] = extractorEntry with { Response = ext.Ok ? (ext.Text ?? "(sin texto)") : $"[Sin Ok] {ext.Error}" };
        if (!ext.Ok || string.IsNullOrWhiteSpace(ext.Text)) { return; }

        // El consumo de la extraccion tambien se carga al tenant, marcado como source "cache".
        await _usage.RecordAsync(agentId, provider, model, ext.InputTokens, ext.OutputTokens, "cache", true, ct);

        var json = StripJsonFromMarkdown(ext.Text!);
        Dictionary<string, JsonElement>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        }
        catch
        {
            return;
        }
        if (parsed is null || parsed.Count == 0) { return; }

        var fieldKeys = fields.Select(f => f.FieldKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, valEl) in parsed)
        {
            if (!fieldKeys.Contains(key)) { continue; }
            string? v = valEl.ValueKind switch
            {
                JsonValueKind.String => valEl.GetString(),
                JsonValueKind.Number => valEl.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                // Arrays: el LLM a veces devuelve listas (ej. destinos = ["Cartagena","Cancun"]). Serializamos como CSV.
                JsonValueKind.Array => string.Join(", ", valEl.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText())
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
                // Objetos: los serializamos en JSON compacto (poco comun, pero mejor capturar algo que perderlo).
                JsonValueKind.Object => valEl.GetRawText(),
                _ => null
            };
            if (string.IsNullOrWhiteSpace(v)) { continue; }
            if (string.Equals(v.Trim(), "PENDIENTE", StringComparison.OrdinalIgnoreCase)) { continue; }
            try { await _cache.SetValueAsync(new SetAgentCacheValueRequest(agentId, sessionId, key, v.Trim(), "inference"), ct); }
            catch { /* la falla de un campo no debe abortar el resto */ }
        }
    }

    /// <summary>Quita envoltorio markdown (```json ... ```) y deja solo el JSON.</summary>
    private static string StripJsonFromMarkdown(string text)
    {
        var t = text.Trim();
        // Quita fences ``` o ```json
        t = Regex.Replace(t, @"^```(?:json)?\s*\n?", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\n?```\s*$", "");
        // Si el modelo agrego prosa antes y el JSON aparece despues, intentamos recortar al primer { y ultimo }.
        var i = t.IndexOf('{');
        var j = t.LastIndexOf('}');
        if (i >= 0 && j > i) { t = t.Substring(i, j - i + 1); }
        return t.Trim();
    }

    // Reemplaza {{nombre}} por la instruccion de entregar ese recurso de forma EXACTA (sin degradarlo).
    private static string ExpandResourceRefs(string text, IReadOnlyList<AiChatAttachment> resources)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{")) { return text; }
        return Regex.Replace(text, @"\{\{\s*([^}]+?)\s*\}\}", m =>
        {
            var res = FindResource(resources, m.Groups[1].Value);
            if (res is null) { return m.Value; }
            return $"el recurso \"{res.Name}\" (entregalo EXACTO incluyendo el marcador [[enviar: {res.Name}]]; el sistema agrega su contenido, no lo reescribas)";
        });
    }

    // Extrae los marcadores [[enviar: Nombre]], los quita del texto y devuelve los recursos a adjuntar.
    private static (string, IReadOnlyList<AiChatAttachment>) ExtractAttachments(string text, IReadOnlyList<AiChatAttachment> resources)
    {
        var attachments = new List<AiChatAttachment>();
        var clean = Regex.Replace(text, @"\[\[\s*enviar\s*:\s*([^\]]+?)\s*\]\]", m =>
        {
            var res = FindResource(resources, m.Groups[1].Value);
            if (res is not null && attachments.All(a => a.Name != res.Name)) { attachments.Add(res); }
            return string.Empty;
        }, RegexOptions.IgnoreCase);

        // Limpia espacios/lineas sobrantes que deja el marcador.
        clean = Regex.Replace(clean, @"[ \t]+\n", "\n").Trim();
        return (clean, attachments);
    }

    // Red de seguridad: a veces el modelo escribe la LLAMADA de una herramienta como texto dentro de la
    // respuesta (p.ej. "crear_lead(cliente_nombre='Lina', tipo_cliente='b2b')"). Eso son "las tripas"
    // del agente y NO debe llegar al cliente. Aqui quitamos cualquier "nombre_de_herramienta(args...)" del
    // texto saliente (con o sin backticks), y limpiamos los restos (fences vacios, lineas en blanco de mas).
    private static string StripToolCallArtifacts(string text, IReadOnlyCollection<string> toolNames)
    {
        if (string.IsNullOrEmpty(text) || toolNames.Count == 0) { return text; }
        var clean = text;
        foreach (var name in toolNames)
        {
            // `?nombre( ... )`?  -> los parentesis no anidan en estos leaks; [^)]* basta.
            var pattern = $@"`?\b{Regex.Escape(name)}\s*\([^)]*\)`?";
            clean = Regex.Replace(clean, pattern, string.Empty, RegexOptions.IgnoreCase);
        }
        // Restos: fences de codigo que quedaron vacios, espacios al final de linea y lineas en blanco triples.
        clean = Regex.Replace(clean, @"```[a-zA-Z]*\s*```", string.Empty);
        clean = Regex.Replace(clean, @"[ \t]+\n", "\n");
        clean = Regex.Replace(clean, @"\n{3,}", "\n\n");
        return clean.Trim();
    }

    private static AiChatAttachment? FindResource(IReadOnlyList<AiChatAttachment> resources, string name)
    {
        var key = Normalize(name);
        return resources.FirstOrDefault(r => Normalize(r.Name) == key);
    }

    // Normaliza para comparar nombres: minusculas y sin acentos (asi "politica" == "{{politica}}").
    private static string Normalize(string s)
    {
        var n = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in n)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) { sb.Append(c); }
        }
        return sb.ToString();
    }

    /// <summary>Tupla compacta con los campos de cache que necesita el prompt + la extraccion.</summary>
    private sealed record CacheFieldInfo(string FieldKey, string Label, string? Description, bool IsUpdatable);

    private static string Truncate(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "...");

    /// <summary>
    /// Construye la linea de un turno del agente para la transcripcion: el texto literal que el agente
    /// escribio + el contenido COMPLETO de los recursos que adjunto. No truncamos: el extractor necesita
    /// ver toda la informacion que el cliente recibio para inferir bien los datos.
    /// </summary>
    private static string BuildTurnLine(string text, IReadOnlyList<AiChatAttachment>? attachments)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(text)) { sb.Append(text.Trim()); }
        if (attachments is { Count: > 0 })
        {
            foreach (var a in attachments)
            {
                if (sb.Length > 0) { sb.AppendLine(); }
                var desc = string.IsNullOrWhiteSpace(a.Detail) ? a.ResourceType.ToString() : a.Detail!.Trim();
                sb.Append($"[envio el recurso \"{a.Name}\" ({a.ResourceType}). Contenido: {desc}]");
            }
        }
        return sb.Length == 0 ? "(turno vacio)" : sb.ToString();
    }

    /// <summary>
    /// Reemplaza los markers [[enviar: Nombre]] del texto crudo del agente por una version legible
    /// que incluye el nombre del recurso y su contenido COMPLETO. Sin esto el extractor no entiende
    /// que comunico el agente y la inferencia de cache falla.
    /// </summary>
    private static string ExpandMarkersForTranscript(string rawText, IReadOnlyList<AiChatAttachment> resources)
    {
        if (string.IsNullOrEmpty(rawText)) { return "(respuesta vacia)"; }
        var expanded = Regex.Replace(rawText, @"\[\[\s*enviar\s*:\s*([^\]]+?)\s*\]\]", m =>
        {
            var res = FindResource(resources, m.Groups[1].Value);
            if (res is null) { return $"[envio el recurso \"{m.Groups[1].Value.Trim()}\"]"; }
            var desc = string.IsNullOrWhiteSpace(res.Detail) ? res.ResourceType.ToString() : res.Detail!.Trim();
            return $"[envio el recurso \"{res.Name}\" ({res.ResourceType}). Contenido: {desc}]";
        }, RegexOptions.IgnoreCase);
        expanded = Regex.Replace(expanded, @"[ \t]+\n", "\n").Trim();
        return string.IsNullOrEmpty(expanded) ? "(respuesta vacia)" : expanded;
    }
}
