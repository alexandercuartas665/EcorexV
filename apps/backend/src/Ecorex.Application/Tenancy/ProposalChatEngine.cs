using System.Text.Json;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

// ==== Motor GENERICO de chat "proponer y confirmar" (ADR-0130) ====
// Generaliza la maquinaria del asistente de formularios (FormBuilderChatService) para cualquier caso de uso
// que tenga un toolset + un prompt de sistema: el modelo pide herramientas; las de SOLO LECTURA corren solas
// y las MUTANTES quedan como PROPUESTAS pendientes hasta que el humano Confirma o Rechaza. La conversacion
// vive en memoria (el llamador decide donde guardarla: por circuito Blazor, por cache, etc.).

/// <summary>Tipo de entrada del hilo.</summary>
public enum ProposalChatEntryKind { User, Assistant, Proposal }

/// <summary>Estado de una propuesta (llamada a herramienta pedida por el modelo).</summary>
public enum ProposalChatState { Pending, Confirmed, Rejected }

/// <summary>Una entrada del hilo: texto del usuario / del asistente, o una propuesta (tool call) con su estado y
/// resultado. Las lecturas se guardan como propuestas AUTO-confirmadas para reconstruir el hilo del proveedor.</summary>
public sealed class ProposalChatEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int Sequence { get; init; }
    public ProposalChatEntryKind Kind { get; init; }
    public string? Text { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }
    public string? ToolArgsJson { get; set; }
    public string? ToolResultJson { get; set; }
    public ProposalChatState State { get; set; }
}

/// <summary>Conversacion en memoria. No es thread-safe: un hilo por conversacion (un panel de chat).</summary>
public sealed class ProposalConversation
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public List<ProposalChatEntry> Entries { get; } = new();

    public IEnumerable<ProposalChatEntry> Pending
        => Entries.Where(e => e.Kind == ProposalChatEntryKind.Proposal && e.State == ProposalChatState.Pending);

    internal ProposalChatEntry Add(ProposalChatEntryKind kind, string? text)
    {
        var e = new ProposalChatEntry { Sequence = NextSeq(), Kind = kind, Text = text };
        Entries.Add(e);
        return e;
    }

    internal ProposalChatEntry AddProposal(AiToolCall tc, ProposalChatState state, string? resultJson)
    {
        var e = new ProposalChatEntry
        {
            Sequence = NextSeq(),
            Kind = ProposalChatEntryKind.Proposal,
            ToolCallId = tc.Id,
            ToolName = tc.Name,
            ToolArgsJson = tc.ArgumentsJson,
            ToolResultJson = resultJson,
            State = state
        };
        Entries.Add(e);
        return e;
    }

    private int NextSeq() => Entries.Count == 0 ? 1 : Entries[^1].Sequence + 1;
}

/// <summary>Como correr un turno: proveedor/clave/modelo, prompt de sistema, toolset y que herramientas son de
/// solo lectura. <see cref="RewriteArgs"/> permite al caso de uso inyectar/forzar argumentos (p.ej. el id del
/// contenedor) ANTES de ejecutar cualquier herramienta, para que el modelo no pueda apuntar a otro lado.</summary>
public sealed record ProposalChatSettings(
    AiProvider Provider,
    string ApiKey,
    string? BaseUrl,
    string Model,
    string SystemPrompt,
    IAgentToolset Toolset,
    IReadOnlySet<string> ReadOnlyTools,
    string UsageSource,
    Func<string, string, string>? RewriteArgs = null);

/// <summary>Una propuesta pendiente (accion mutante) que espera Confirmar/Rechazar.</summary>
public sealed record ProposalDto(Guid Id, string ToolName, string ArgumentsJson);

/// <summary>Una herramienta EJECUTADA en este turno (lectura automatica o propuesta confirmada) con su resultado.</summary>
public sealed record ToolRunDto(string ToolName, string ArgumentsJson, string ResultJson);

/// <summary>Resultado de un turno (enviar / confirmar / rechazar).</summary>
public sealed record ProposalChatTurn(
    bool Ok,
    string? Error,
    string? AssistantText,
    IReadOnlyList<ProposalDto> Proposals,
    bool AwaitingConfirmation,
    IReadOnlyList<ToolRunDto> ToolRuns,
    int InputTokens = 0,
    int OutputTokens = 0,
    int CachedInputTokens = 0,
    decimal CostUsd = 0m)
{
    public static ProposalChatTurn Fail(string error, IReadOnlyList<ToolRunDto>? runs = null)
        => new(false, error, null, Array.Empty<ProposalDto>(), false, runs ?? Array.Empty<ToolRunDto>());
}

public interface IProposalChatEngine
{
    /// <summary>Turno del usuario: descarta propuestas pendientes (escribir = no confirmar), agrega el mensaje y corre el agente.</summary>
    Task<ProposalChatTurn> SendAsync(ProposalConversation conv, ProposalChatSettings settings, string userText, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Ejecuta las propuestas pendientes en orden y continua el agente.</summary>
    Task<ProposalChatTurn> ConfirmAsync(ProposalConversation conv, ProposalChatSettings settings, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Marca rechazadas las pendientes, deja constancia y continua el agente.</summary>
    Task<ProposalChatTurn> RejectAsync(ProposalConversation conv, ProposalChatSettings settings, string? reason, Guid actorUserId, CancellationToken ct = default);
}

public sealed class ProposalChatEngine : IProposalChatEngine
{
    // Tope de vueltas al modelo por turno: evita ciclos si el modelo insiste con lecturas.
    private const int MaxRounds = 8;
    // Timeout por llamada al modelo: un cuelgue debe fallar limpio en vez de congelar el panel.
    private static readonly TimeSpan AiCallTimeout = TimeSpan.FromSeconds(90);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IAiProviderClient _ai;
    private readonly IAiUsageService _usage;

    public ProposalChatEngine(IAiProviderClient ai, IAiUsageService usage)
    {
        _ai = ai;
        _usage = usage;
    }

    public async Task<ProposalChatTurn> SendAsync(ProposalConversation conv, ProposalChatSettings settings, string userText, Guid actorUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userText)) { return ProposalChatTurn.Fail("Escribe un mensaje."); }

        // Escribir con una propuesta pendiente = NO confirmarla: se descarta y se atiende lo nuevo.
        var pending = conv.Pending.ToList();
        if (pending.Count > 0)
        {
            foreach (var p in pending) { p.State = ProposalChatState.Rejected; }
            conv.Add(ProposalChatEntryKind.User, "(No confirme la propuesta anterior: descartala y atiende lo que pido a continuacion.)");
        }
        conv.Add(ProposalChatEntryKind.User, userText.Trim());
        return await RunAsync(conv, settings, actorUserId, Array.Empty<ToolRunDto>(), ct);
    }

    public async Task<ProposalChatTurn> ConfirmAsync(ProposalConversation conv, ProposalChatSettings settings, Guid actorUserId, CancellationToken ct = default)
    {
        var pending = conv.Pending.OrderBy(p => p.Sequence).ToList();
        if (pending.Count == 0) { return ProposalChatTurn.Fail("No hay acciones pendientes por confirmar."); }

        var runs = new List<ToolRunDto>();
        foreach (var p in pending)
        {
            var args = Rewrite(settings, p.ToolName ?? "", p.ToolArgsJson ?? "{}");
            p.ToolArgsJson = args;
            p.ToolResultJson = await ExecuteSafeAsync(settings, p.ToolName ?? "", args, actorUserId, ct);
            p.State = ProposalChatState.Confirmed;
            runs.Add(new ToolRunDto(p.ToolName ?? "", args, p.ToolResultJson));
        }
        return await RunAsync(conv, settings, actorUserId, runs, ct);
    }

    public async Task<ProposalChatTurn> RejectAsync(ProposalConversation conv, ProposalChatSettings settings, string? reason, Guid actorUserId, CancellationToken ct = default)
    {
        var pending = conv.Pending.ToList();
        if (pending.Count == 0) { return ProposalChatTurn.Fail("No hay acciones pendientes por rechazar."); }
        foreach (var p in pending) { p.State = ProposalChatState.Rejected; }
        conv.Add(ProposalChatEntryKind.User, string.IsNullOrWhiteSpace(reason)
            ? "El usuario rechazo la accion propuesta. Propon una alternativa o pregunta que ajustar."
            : $"El usuario rechazo la accion propuesta. Motivo: {reason}. Propon una alternativa o pregunta que ajustar.");
        return await RunAsync(conv, settings, actorUserId, Array.Empty<ToolRunDto>(), ct);
    }

    // ===== Nucleo: bucle del agente con GATE humano =====
    private async Task<ProposalChatTurn> RunAsync(ProposalConversation conv, ProposalChatSettings s, Guid actorUserId, IReadOnlyList<ToolRunDto> priorRuns, CancellationToken ct)
    {
        var runs = priorRuns.ToList();

        // CUPO POR PLAN (misma regla que los demas agentes): con limite DURO agotado no se llama al modelo.
        var quota = await _usage.GetQuotaAsync(ct);
        if (quota.Exceeded && quota.Hard)
        {
            return ProposalChatTurn.Fail($"Alcanzaste el limite de tokens de IA de tu plan este mes ({quota.MonthlyLimitTokens:N0}). El asistente queda pausado hasta el proximo ciclo o hasta ampliar el plan.", runs);
        }

        var tools = s.Toolset.GetSpecs();
        var turnIn = 0;
        var turnOut = 0;
        var turnCached = 0;

        for (var round = 0; round < MaxRounds; round++)
        {
            var messages = BuildProviderMessages(conv);
            AiCompletion completion;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(AiCallTimeout);
                completion = await _ai.CompleteWithToolsAsync(s.Provider, s.ApiKey, s.BaseUrl, s.Model, s.SystemPrompt, messages, tools, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return ProposalChatTurn.Fail("La IA tardo demasiado en responder. Intenta de nuevo o simplifica la instruccion.", runs);
            }
            catch (Exception ex)
            {
                return ProposalChatTurn.Fail("Error hablando con la IA: " + ex.Message, runs);
            }
            if (!completion.Ok) { return ProposalChatTurn.Fail(completion.Error ?? "La IA no respondio.", runs); }

            // Nombres de herramienta NORMALIZADOS: Gemini (via la API compatible con OpenAI) a veces devuelve el
            // nombre con un prefijo de espacio de nombres inventado ("ecorex.proponer_derivacion"); sin esto la
            // lectura no casaba con ReadOnlyTools y caia al gate como si fuera mutante, y la ejecucion fallaba.
            var toolNames = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
            completion = completion with
            {
                ToolCalls = (completion.ToolCalls ?? Array.Empty<AiToolCall>())
                    .Select(tc => tc with { Name = NormalizeToolName(tc.Name, toolNames) }).ToList()
            };

            // Consumo: cada vuelta cuenta y se registra en AiUsageLog con la fuente del caso de uso.
            turnIn += completion.InputTokens;
            turnOut += completion.OutputTokens;
            turnCached += completion.CachedInputTokens;
            try { await _usage.RecordAsync(null, s.Provider, s.Model, completion.InputTokens, completion.OutputTokens, s.UsageSource, true, ct); }
            catch { /* la telemetria nunca frena el chat */ }

            var cost = AiCostEstimator.Estimate(s.Provider, s.Model, turnIn, turnOut, turnCached);

            // Turno solo texto: cierre del turno.
            if (completion.ToolCalls is null || completion.ToolCalls.Count == 0)
            {
                conv.Add(ProposalChatEntryKind.Assistant, completion.Text);
                return new ProposalChatTurn(true, null, completion.Text, Array.Empty<ProposalDto>(), false, runs, turnIn, turnOut, turnCached, cost);
            }

            // Todas de solo lectura: se ejecutan sin gate y sigue el bucle.
            if (completion.ToolCalls.All(tc => s.ReadOnlyTools.Contains(tc.Name)))
            {
                if (!string.IsNullOrWhiteSpace(completion.Text)) { conv.Add(ProposalChatEntryKind.Assistant, completion.Text); }
                foreach (var tc in completion.ToolCalls)
                {
                    var args = Rewrite(s, tc.Name, tc.ArgumentsJson);
                    var result = await ExecuteSafeAsync(s, tc.Name, args, actorUserId, ct);
                    conv.AddProposal(tc with { ArgumentsJson = args }, ProposalChatState.Confirmed, result);
                    runs.Add(new ToolRunDto(tc.Name, args, result));
                }
                continue;
            }

            // Al menos una MUTANTE: se propone todo el lote y se detiene (gate humano).
            if (!string.IsNullOrWhiteSpace(completion.Text)) { conv.Add(ProposalChatEntryKind.Assistant, completion.Text); }
            var proposals = new List<ProposalDto>();
            foreach (var tc in completion.ToolCalls)
            {
                var args = Rewrite(s, tc.Name, tc.ArgumentsJson);
                var e = conv.AddProposal(tc with { ArgumentsJson = args }, ProposalChatState.Pending, null);
                proposals.Add(new ProposalDto(e.Id, tc.Name, args));
            }
            return new ProposalChatTurn(true, null, completion.Text, proposals, true, runs, turnIn, turnOut, turnCached, cost);
        }

        return ProposalChatTurn.Fail("El asistente hizo demasiadas consultas seguidas. Intenta de nuevo o precisa la instruccion.", runs);
    }

    /// <summary>Si el nombre no es una herramienta conocida pero su ultimo segmento tras un punto si lo es
    /// ("ecorex.describir_tabla" -> "describir_tabla"), devuelve el conocido; si no, el original.</summary>
    internal static string NormalizeToolName(string name, IReadOnlySet<string> known)
    {
        if (string.IsNullOrWhiteSpace(name) || known.Contains(name)) { return name; }
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1)
        {
            var tail = name[(dot + 1)..];
            if (known.Contains(tail)) { return tail; }
        }
        return name;
    }

    private static string Rewrite(ProposalChatSettings s, string toolName, string argsJson)
    {
        if (s.RewriteArgs is null) { return argsJson; }
        try { return s.RewriteArgs(toolName, argsJson); }
        catch { return argsJson; }
    }

    private static async Task<string> ExecuteSafeAsync(ProposalChatSettings s, string toolName, string argsJson, Guid actorUserId, CancellationToken ct)
    {
        try
        {
            var r = await s.Toolset.ExecuteAsync(toolName, argsJson, actorUserId, autonomous: true, ct);
            return r.Json;
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = ex.Message }, Json);
        }
    }

    /// <summary>Reconstruye el hilo para el proveedor: user/assistant tal cual; las propuestas CONFIRMADAS
    /// contiguas se agrupan en un turno assistant con tool-calls + sus resultados; Pending/Rejected no viajan.</summary>
    internal static List<AiToolMessage> BuildProviderMessages(ProposalConversation conv)
    {
        var ordered = conv.Entries.OrderBy(e => e.Sequence).ToList();
        var outMsgs = new List<AiToolMessage>();
        var i = 0;
        while (i < ordered.Count)
        {
            var e = ordered[i];
            switch (e.Kind)
            {
                case ProposalChatEntryKind.User:
                    outMsgs.Add(new AiToolMessage("user", e.Text ?? string.Empty));
                    i++;
                    break;
                case ProposalChatEntryKind.Assistant:
                    outMsgs.Add(new AiToolMessage("assistant", e.Text ?? string.Empty));
                    i++;
                    break;
                case ProposalChatEntryKind.Proposal when e.State == ProposalChatState.Confirmed:
                    {
                        var run = new List<ProposalChatEntry>();
                        while (i < ordered.Count && ordered[i].Kind == ProposalChatEntryKind.Proposal && ordered[i].State == ProposalChatState.Confirmed)
                        {
                            run.Add(ordered[i]);
                            i++;
                        }
                        var calls = run.Select(p => new AiToolCall(p.ToolCallId ?? p.Id.ToString("N"), p.ToolName ?? string.Empty, p.ToolArgsJson ?? "{}")).ToList();
                        outMsgs.Add(new AiToolMessage("assistant", null, calls));
                        foreach (var p in run)
                        {
                            outMsgs.Add(new AiToolMessage("tool", p.ToolResultJson ?? "{}", null, p.ToolCallId ?? p.Id.ToString("N"), p.ToolName));
                        }
                        break;
                    }
                default:
                    i++;
                    break;
            }
        }
        return outMsgs;
    }
}
