using System.Text;
using System.Text.Json;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Forms.Builder;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Gestor.TemplateAgent;

/// <summary>
/// NUCLEO GENERICO del agente editor de plantillas (ADR-0174). Mismo patron que el constructor de formularios
/// (FormBuilderChatService, ADR-0067) pero independiente del tipo de plantilla: el toolset de cada tipo
/// (correo / WhatsApp) aporta arnes, herramientas, verificador y version. El nucleo aporta:
///  - conversacion persistente (se retoma al reabrir la plantilla);
///  - GATE HUMANO: lecturas sin gate; mutaciones se PROPONEN y corren tras Confirmar; las IRREVERSIBLES
///    (someter a Meta) exigen ademas la confirmacion explicita aparte;
///  - version (snapshot) antes de cada lote que modifica la plantilla, con Restaurar;
///  - proveedor config-driven ("IA gestion de plantillas" -> formularios -> Gemini) y cupo por plan;
///  - verificacion DETERMINISTA obligatoria al cerrar el turno (la corre el sistema, no el modelo).
/// No se extrajo el bucle de formularios (tiene autocuracion de ids de contenedor propia de ese dominio);
/// este nucleo queda listo para que formularios migre despues.
/// </summary>
public sealed class TemplateAgentChatService : ITemplateAgentChatService
{
    private const AiProvider DefaultProvider = AiProvider.Gemini;
    private const int MaxRounds = 8;
    private const int MaxForcedVerify = 2;
    private static readonly TimeSpan AiCallTimeout = TimeSpan.FromSeconds(120);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ISecretProtector _secrets;
    private readonly IAiProviderClient _ai;
    private readonly ITemplateAgentStore _store;
    private readonly ITemplateSnapshotService _snapshots;
    private readonly IAiUsageService _usage;
    private readonly IReadOnlyDictionary<TemplateAgentKind, ITemplateAgentToolset> _toolsets;

    public TemplateAgentChatService(
        ISecretProtector secrets, IAiProviderClient ai, ITemplateAgentStore store,
        ITemplateSnapshotService snapshots, IAiUsageService usage, IEnumerable<ITemplateAgentToolset> toolsets)
    {
        _secrets = secrets;
        _ai = ai;
        _store = store;
        _snapshots = snapshots;
        _usage = usage;
        _toolsets = toolsets.ToDictionary(t => t.Kind);
    }

    // Modelo rapido y con function-calling por proveedor (el chat da muchas vueltas).
    private static string DefaultModelFor(AiProvider provider) => provider switch
    {
        AiProvider.Gemini => "gemini-2.5-flash",
        AiProvider.DeepSeek => "deepseek-chat",
        AiProvider.ChatGpt => "gpt-4o-mini",
        AiProvider.Claude => "claude-haiku-4-5",
        _ => AiProviderCatalog.For(provider).DefaultModel,
    };

    private static string ChooseModel(AiProvider provider, string? configured)
    {
        var m = configured?.Trim();
        if (!string.IsNullOrWhiteSpace(m))
        {
            var x = m!.ToLowerInvariant();
            var nonTool = x.Contains("reasoner") || x.Contains("-thinking") || x.Contains("o1-") || x.EndsWith("-o1");
            if (!nonTool) { return m!; }
        }
        return DefaultModelFor(provider);
    }

    public async Task<TemplateAgentStartResult> StartAsync(TemplateAgentKind kind, Guid? templateId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (!_toolsets.TryGetValue(kind, out var toolset))
        {
            return new TemplateAgentStartResult(false, $"El agente de plantillas de {kind} no esta disponible.", Guid.Empty, null);
        }
        if (templateId is Guid tid)
        {
            var existing = await _store.GetLatestConversationAsync(kind, tid, cancellationToken);
            if (existing is not null) { return new TemplateAgentStartResult(true, null, existing.Id, existing.TemplateId); }
        }
        return await CreateAsync(kind, toolset, templateId, actorUserId, cancellationToken);
    }

    private async Task<TemplateAgentStartResult> CreateAsync(TemplateAgentKind kind, ITemplateAgentToolset toolset, Guid? templateId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var provider = await _store.GetTemplateBuilderProviderAsync(cancellationToken) ?? DefaultProvider;
        var cfg = await _store.ResolveProviderAsync(provider, cancellationToken);
        if (cfg is null || !cfg.Enabled || string.IsNullOrWhiteSpace(cfg.ApiKeyEncrypted))
        {
            return new TemplateAgentStartResult(false,
                $"El proveedor de IA para plantillas ({provider}) no esta habilitado. Configuralo en Servidores de IA (IA gestion de plantillas).",
                Guid.Empty, null);
        }
        var title = kind == TemplateAgentKind.Email ? "Nueva plantilla de correo" : "Nueva plantilla de WhatsApp";
        if (templateId is Guid tid && await toolset.GetTitleAsync(tid, cancellationToken) is { } t && !string.IsNullOrWhiteSpace(t))
        {
            title = t;
        }
        var conv = await _store.CreateConversationAsync(kind, templateId, provider, ChooseModel(provider, cfg.Model), title, actorUserId, cancellationToken);
        return new TemplateAgentStartResult(true, null, conv.Id, conv.TemplateId);
    }

    public async Task<TemplateAgentStartResult> ResetAsync(Guid conversationId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var conv = await _store.GetConversationAsync(conversationId, cancellationToken);
        if (conv is null || !_toolsets.TryGetValue(conv.Kind, out var toolset))
        {
            return new TemplateAgentStartResult(false, "La conversacion no existe.", Guid.Empty, null);
        }
        conv.Status = FormBuilderConversationStatus.Closed;
        await _store.SaveConversationAsync(conv, cancellationToken);
        return await CreateAsync(conv.Kind, toolset, conv.TemplateId, actorUserId, cancellationToken);
    }

    public async Task NoteExternalChangeAsync(TemplateAgentKind kind, Guid templateId, string what, CancellationToken cancellationToken = default)
    {
        var conv = await _store.GetLatestConversationAsync(kind, templateId, cancellationToken);
        if (conv is null) { return; }
        await AddMessageAsync(conv, FormBuilderMessageRole.User,
            $"{TemplateAgentNotes.Prefix} {what} El contenido vigente es el que devuelve get_template; NO reapliques cambios anteriores del chat salvo que el usuario lo pida de nuevo.)",
            null, cancellationToken);
    }

    public async Task<IReadOnlyList<TemplateAgentMessage>> GetTranscriptAsync(Guid conversationId, CancellationToken cancellationToken = default)
        => await _store.GetMessagesAsync(conversationId, cancellationToken);

    public async Task<TemplateAgentTurnResult> SendAsync(Guid conversationId, string? userText, IReadOnlyList<TemplateAgentAttachment>? attachments, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var conv = await _store.GetConversationAsync(conversationId, cancellationToken);
        if (conv is null) { return TemplateAgentTurnResult.Fail(conversationId, "La conversacion no existe."); }

        // Escribir con una propuesta pendiente = no la confirma: se descarta y se atiende el nuevo mensaje.
        await DiscardPendingAsync(conv, cancellationToken);

        var images = new List<AiInlineImage>();
        var docs = new List<AiInlineDocument>();
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(userText)) { sb.Append(userText!.Trim()); }
        var meta = new List<object>();
        foreach (var a in attachments ?? Array.Empty<TemplateAgentAttachment>())
        {
            if (a is null || string.IsNullOrWhiteSpace(a.Base64)) { continue; }
            meta.Add(new { a.FileName, a.Mime });
            var mime = a.Mime ?? string.Empty;
            if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                images.Add(new AiInlineImage(a.Base64, mime));
            }
            else if (mime.Contains("pdf", StringComparison.OrdinalIgnoreCase) || a.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                docs.Add(new AiInlineDocument(a.Base64, string.IsNullOrWhiteSpace(mime) ? "application/pdf" : mime, a.FileName));
            }
            else if (mime.Contains("html", StringComparison.OrdinalIgnoreCase) || mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || a.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || a.FileName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)
                || a.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                var text = DecodeText(a.Base64);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.Append("\n\n[Contenido del archivo ").Append(a.FileName).Append("]\n").Append(Truncate(text, 60000));
                }
            }
        }

        var content = sb.ToString();
        if (string.IsNullOrWhiteSpace(content) && images.Count == 0 && docs.Count == 0)
        {
            return TemplateAgentTurnResult.Fail(conversationId, "Escribe un mensaje o adjunta un archivo.");
        }
        if (string.IsNullOrWhiteSpace(content))
        {
            content = "(Adjunto un archivo de referencia: replicalo como plantilla.)";
        }

        await AddMessageAsync(conv, FormBuilderMessageRole.User, content, meta.Count > 0 ? JsonSerializer.Serialize(meta, Json) : null, cancellationToken);
        return await RunAgentAsync(conv, images, docs, actorUserId, false, cancellationToken);
    }

    public async Task<TemplateAgentTurnResult> ConfirmAsync(Guid conversationId, Guid actorUserId, bool confirmIrreversible = false, CancellationToken cancellationToken = default)
    {
        var conv = await _store.GetConversationAsync(conversationId, cancellationToken);
        if (conv is null || !_toolsets.TryGetValue(conv.Kind, out var toolset))
        {
            return TemplateAgentTurnResult.Fail(conversationId, "La conversacion no existe.");
        }
        var pending = (await _store.GetMessagesAsync(conversationId, cancellationToken))
            .Where(m => m.Role == FormBuilderMessageRole.Proposal && m.ProposalState == FormBuilderProposalState.Pending)
            .OrderBy(m => m.Sequence).ToList();
        if (pending.Count == 0) { return TemplateAgentTurnResult.Fail(conversationId, "No hay acciones pendientes por confirmar."); }

        // Irreversibles (someter a Meta): sin la confirmacion explicita aparte NO se ejecuta nada del lote.
        if (!confirmIrreversible && pending.Any(p => toolset.IrreversibleTools.Contains(p.ToolName ?? string.Empty)))
        {
            return TemplateAgentTurnResult.Fail(conversationId,
                "Esta accion es irreversible (se envia a Meta para aprobacion). Confirmala con el boton especifico.");
        }

        // VERSION antes de aplicar un lote que modifica la plantilla (red de seguridad; nunca frena).
        if (conv.TemplateId is Guid snapId && pending.Any(p => toolset.TemplateMutatingTools.Contains(p.ToolName ?? string.Empty)))
        {
            try
            {
                var tools = string.Join(", ", pending.Select(p => p.ToolName).Distinct());
                await _snapshots.SnapshotAsync(conv.Kind, snapId, $"Antes de: {tools}", conv.Id, actorUserId, cancellationToken);
            }
            catch { /* best-effort */ }
        }

        var ctx = new TemplateAgentToolContext { ConversationId = conv.Id, TemplateId = conv.TemplateId, ActorUserId = actorUserId };
        foreach (var p in pending)
        {
            string resultJson;
            try { resultJson = await toolset.ExecuteAsync(ctx, p.ToolName ?? string.Empty, p.ToolArgsJson ?? "{}", cancellationToken); }
            catch (Exception ex) { resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message }, Json); }
            p.ProposalState = FormBuilderProposalState.Confirmed;
            p.ToolResultJson = resultJson;
            await _store.SaveMessageAsync(p, cancellationToken);
        }

        // El toolset pudo CREAR la plantilla: se liga la conversacion para editar en vivo.
        if (ctx.TemplateId != conv.TemplateId)
        {
            conv.TemplateId = ctx.TemplateId;
            if (ctx.TemplateId is Guid newId && await toolset.GetTitleAsync(newId, cancellationToken) is { } title && !string.IsNullOrWhiteSpace(title))
            {
                conv.Title = title.Length > 200 ? title[..200] : title;
            }
            await _store.SaveConversationAsync(conv, cancellationToken);
        }

        return await RunAgentAsync(conv, null, null, actorUserId, ctx.TemplateChanged, cancellationToken);
    }

    public async Task<TemplateAgentTurnResult> RejectAsync(Guid conversationId, string? reason, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var conv = await _store.GetConversationAsync(conversationId, cancellationToken);
        if (conv is null) { return TemplateAgentTurnResult.Fail(conversationId, "La conversacion no existe."); }
        var pending = (await _store.GetMessagesAsync(conversationId, cancellationToken))
            .Where(m => m.Role == FormBuilderMessageRole.Proposal && m.ProposalState == FormBuilderProposalState.Pending).ToList();
        if (pending.Count == 0) { return TemplateAgentTurnResult.Fail(conversationId, "No hay acciones pendientes por rechazar."); }
        foreach (var p in pending)
        {
            p.ProposalState = FormBuilderProposalState.Rejected;
            await _store.SaveMessageAsync(p, cancellationToken);
        }
        var note = string.IsNullOrWhiteSpace(reason)
            ? "El usuario rechazo la accion propuesta. Propon una alternativa o pregunta que ajustar."
            : $"El usuario rechazo la accion propuesta. Motivo: {reason}. Propon una alternativa o pregunta que ajustar.";
        await AddMessageAsync(conv, FormBuilderMessageRole.User, note, null, cancellationToken);
        return await RunAgentAsync(conv, null, null, actorUserId, false, cancellationToken);
    }

    private async Task DiscardPendingAsync(TemplateAgentConversation conv, CancellationToken ct)
    {
        var pending = (await _store.GetMessagesAsync(conv.Id, ct))
            .Where(m => m.Role == FormBuilderMessageRole.Proposal && m.ProposalState == FormBuilderProposalState.Pending).ToList();
        if (pending.Count == 0) { return; }
        foreach (var p in pending)
        {
            p.ProposalState = FormBuilderProposalState.Rejected;
            await _store.SaveMessageAsync(p, ct);
        }
        await AddMessageAsync(conv, FormBuilderMessageRole.User,
            "(No confirme la propuesta anterior: descartala y atiende lo que pido a continuacion.)", null, ct);
    }

    // ===== Bucle del agente con gate humano =====
    private async Task<TemplateAgentTurnResult> RunAgentAsync(TemplateAgentConversation conv, IReadOnlyList<AiInlineImage>? images,
        IReadOnlyList<AiInlineDocument>? docs, Guid actorUserId, bool templateChanged, CancellationToken cancellationToken)
    {
        if (!_toolsets.TryGetValue(conv.Kind, out var toolset))
        {
            return TemplateAgentTurnResult.Fail(conv.Id, "Tipo de plantilla no soportado.");
        }
        var provider = conv.Provider;
        var cfg = await _store.ResolveProviderAsync(provider, cancellationToken);
        if (cfg is null || !cfg.Enabled || string.IsNullOrWhiteSpace(cfg.ApiKeyEncrypted))
        {
            return TemplateAgentTurnResult.Fail(conv.Id, $"El proveedor de IA {provider} no esta habilitado. Revisa Servidores de IA.");
        }
        string apiKey;
        try { apiKey = _secrets.Unprotect(cfg.ApiKeyEncrypted!); }
        catch { return TemplateAgentTurnResult.Fail(conv.Id, "La API key del proveedor esta cifrada con una version anterior. Vuelve a guardarla en Servidores de IA."); }

        var model = !string.IsNullOrWhiteSpace(conv.Model) ? conv.Model! : DefaultModelFor(provider);
        var baseUrl = !string.IsNullOrWhiteSpace(cfg.BaseUrl) ? cfg.BaseUrl : AiProviderCatalog.For(provider).DefaultBaseUrl;

        // Cupo por plan (misma regla que los demas agentes): limite DURO agotado -> no se llama al modelo.
        var quota = await _usage.GetQuotaAsync(cancellationToken);
        if (quota.Exceeded && quota.Hard)
        {
            return TemplateAgentTurnResult.Fail(conv.Id,
                $"Alcanzaste el limite de tokens de IA de tu plan este mes ({quota.MonthlyLimitTokens:N0}). El agente de plantillas queda pausado hasta el proximo ciclo o hasta ampliar el plan.");
        }

        var tenantName = await _store.GetTenantNameAsync(conv.TenantId, cancellationToken);
        if (string.IsNullOrWhiteSpace(tenantName)) { tenantName = "tu empresa"; }
        var systemPrompt = await toolset.SystemPromptAsync(tenantName, conv.TemplateId, cancellationToken);
        var tools = toolset.GetSpecs();

        string? nudge = null;
        var forcedVerify = 0;
        var nudgedToAct = false;
        var turnIn = 0; var turnOut = 0; var turnCached = 0;

        for (var round = 0; round < MaxRounds; round++)
        {
            var stored = await _store.GetMessagesAsync(conv.Id, cancellationToken);
            var messages = BuildProviderMessages(stored, images, docs);
            if (nudge is not null) { messages.Add(new AiToolMessage("user", nudge)); nudge = null; }

            AiCompletion completion;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(AiCallTimeout);
                completion = await _ai.CompleteWithToolsAsync(provider, apiKey, baseUrl, model, systemPrompt, messages, tools, cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return TemplateAgentTurnResult.Fail(conv.Id, "La IA tardo demasiado en responder. Intenta de nuevo o simplifica la instruccion.");
            }
            catch (Exception ex)
            {
                return TemplateAgentTurnResult.Fail(conv.Id, "Error hablando con la IA: " + ex.Message);
            }
            if (!completion.Ok) { return TemplateAgentTurnResult.Fail(conv.Id, completion.Error ?? "La IA no respondio."); }

            turnIn += completion.InputTokens; turnOut += completion.OutputTokens; turnCached += completion.CachedInputTokens;
            try { await _usage.RecordAsync(null, provider, model, completion.InputTokens, completion.OutputTokens, "template-builder", true, cancellationToken); }
            catch { /* telemetria: nunca frena el chat */ }

            var cost = AiCostEstimator.Estimate(provider, model, turnIn, turnOut, turnCached);

            // --- Turno solo texto ---
            if (completion.ToolCalls is null || completion.ToolCalls.Count == 0)
            {
                await AddMessageAsync(conv, FormBuilderMessageRole.Assistant, completion.Text, null, cancellationToken);
                // Anti "narra pero no emite" (misma heuristica del constructor de formularios): un empujon por turno.
                if (!nudgedToAct && FormBuilderChatService.StoppedMidAction(completion.Text))
                {
                    nudgedToAct = true;
                    nudge = "Describiste lo que ibas a hacer pero NO emitiste las llamadas a las herramientas. Emitelas AHORA " +
                        "(verify_email / save_template); no lo vuelvas a narrar. Si necesitas un dato del usuario, pidelo en una frase.";
                    continue;
                }
                // Cierre: verificacion OBLIGATORIA forzada por el sistema (si hay plantilla y no es una pregunta).
                if (conv.TemplateId is Guid vid && forcedVerify < MaxForcedVerify && !EndsWithQuestion(completion.Text))
                {
                    TemplateVerifyResult? vr = null;
                    try { vr = await toolset.VerifyAsync(vid, cancellationToken); } catch { /* el verificador no bloquea */ }
                    if (vr is { Errors: > 0 })
                    {
                        forcedVerify++;
                        nudge = "AUTO-VERIFICACION OBLIGATORIA DEL CIERRE (la corrio el SISTEMA). La plantilla guardada tiene " +
                            $"{vr.Errors} error(es) que DEBES corregir antes de cerrar. Propone YA la correccion con la herramienta " +
                            "de guardar (no lo narres):\n" + vr.ErrorDetail();
                        continue;
                    }
                }
                return new TemplateAgentTurnResult(true, null, conv.Id, conv.TemplateId, completion.Text,
                    Array.Empty<TemplateAgentProposalDto>(), false, turnIn, turnOut, cost, turnCached, templateChanged);
            }

            // --- Solo lecturas: se ejecutan sin gate y sigue el bucle ---
            if (completion.ToolCalls.All(tc => toolset.ReadOnlyTools.Contains(tc.Name)))
            {
                if (!string.IsNullOrWhiteSpace(completion.Text))
                {
                    await AddMessageAsync(conv, FormBuilderMessageRole.Assistant, completion.Text, null, cancellationToken);
                }
                var ctx = new TemplateAgentToolContext { ConversationId = conv.Id, TemplateId = conv.TemplateId, ActorUserId = actorUserId };
                foreach (var tc in completion.ToolCalls)
                {
                    string resultJson;
                    try { resultJson = await toolset.ExecuteAsync(ctx, tc.Name, tc.ArgumentsJson, cancellationToken); }
                    catch (Exception ex) { resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message }, Json); }
                    await AddProposalAsync(conv, tc, FormBuilderProposalState.Confirmed, resultJson, cancellationToken);
                }
                images = null; docs = null;
                continue;
            }

            // --- Hay mutaciones: se PROPONE todo el turno y se detiene (gate humano) ---
            if (!string.IsNullOrWhiteSpace(completion.Text))
            {
                await AddMessageAsync(conv, FormBuilderMessageRole.Assistant, completion.Text, null, cancellationToken);
            }
            var proposals = new List<TemplateAgentProposalDto>();
            foreach (var tc in completion.ToolCalls)
            {
                var m = await AddProposalAsync(conv, tc, FormBuilderProposalState.Pending, null, cancellationToken);
                proposals.Add(new TemplateAgentProposalDto(m.Id, tc.Name, tc.ArgumentsJson, toolset.IrreversibleTools.Contains(tc.Name)));
            }
            return new TemplateAgentTurnResult(true, null, conv.Id, conv.TemplateId, completion.Text, proposals, true,
                turnIn, turnOut, cost, turnCached, templateChanged);
        }

        return TemplateAgentTurnResult.Fail(conv.Id, "El agente hizo demasiadas consultas seguidas. Intenta de nuevo o precisa la instruccion.");
    }

    internal static bool EndsWithQuestion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return false; }
        var t = text.TrimEnd();
        return t.EndsWith('?') || t.EndsWith("?\"", StringComparison.Ordinal) || t.EndsWith("?)", StringComparison.Ordinal)
            || t.EndsWith("?**", StringComparison.Ordinal);
    }

    // Reconstruye el historial para el proveedor (las propuestas Pending/Rejected no entran).
    internal static List<AiToolMessage> BuildProviderMessages(IReadOnlyList<TemplateAgentMessage> stored, IReadOnlyList<AiInlineImage>? images, IReadOnlyList<AiInlineDocument>? docs)
    {
        var ordered = stored.OrderBy(m => m.Sequence).ToList();
        var outMsgs = new List<AiToolMessage>();
        var i = 0;
        while (i < ordered.Count)
        {
            var m = ordered[i];
            if (m.Role == FormBuilderMessageRole.User) { outMsgs.Add(new AiToolMessage("user", m.Content ?? string.Empty)); i++; }
            else if (m.Role == FormBuilderMessageRole.Assistant)
            {
                if (!string.IsNullOrWhiteSpace(m.Content)) { outMsgs.Add(new AiToolMessage("assistant", m.Content)); }
                i++;
            }
            else if (m.Role == FormBuilderMessageRole.Proposal && m.ProposalState == FormBuilderProposalState.Confirmed)
            {
                var run = new List<TemplateAgentMessage>();
                while (i < ordered.Count && ordered[i].Role == FormBuilderMessageRole.Proposal && ordered[i].ProposalState == FormBuilderProposalState.Confirmed)
                {
                    run.Add(ordered[i]); i++;
                }
                outMsgs.Add(new AiToolMessage("assistant", null, run.Select(p => new AiToolCall(p.ToolCallId ?? p.Id.ToString("N"), p.ToolName ?? string.Empty, p.ToolArgsJson ?? "{}")).ToList()));
                foreach (var p in run)
                {
                    outMsgs.Add(new AiToolMessage("tool", p.ToolResultJson ?? "{}", null, p.ToolCallId ?? p.Id.ToString("N"), p.ToolName));
                }
            }
            else { i++; }
        }
        if (images is { Count: > 0 } || docs is { Count: > 0 })
        {
            for (var k = outMsgs.Count - 1; k >= 0; k--)
            {
                if (outMsgs[k].Role == "user") { outMsgs[k] = outMsgs[k] with { Images = images, Documents = docs }; break; }
            }
        }
        return outMsgs;
    }

    private async Task<TemplateAgentMessage> AddMessageAsync(TemplateAgentConversation conv, FormBuilderMessageRole role, string? content, string? attachmentsJson, CancellationToken ct)
        => await _store.AddMessageAsync(new TemplateAgentMessage
        {
            ConversationId = conv.Id,
            Sequence = await NextSequenceAsync(conv.Id, ct),
            Role = role,
            Content = content,
            AttachmentsJson = attachmentsJson,
        }, ct);

    private async Task<TemplateAgentMessage> AddProposalAsync(TemplateAgentConversation conv, AiToolCall tc, FormBuilderProposalState state, string? resultJson, CancellationToken ct)
        => await _store.AddMessageAsync(new TemplateAgentMessage
        {
            ConversationId = conv.Id,
            Sequence = await NextSequenceAsync(conv.Id, ct),
            Role = FormBuilderMessageRole.Proposal,
            ToolCallId = tc.Id,
            ToolName = tc.Name,
            ToolArgsJson = tc.ArgumentsJson,
            ToolResultJson = resultJson,
            ProposalState = state,
        }, ct);

    private async Task<int> NextSequenceAsync(Guid conversationId, CancellationToken ct)
    {
        var existing = await _store.GetMessagesAsync(conversationId, ct);
        return existing.Count == 0 ? 1 : existing.Max(m => m.Sequence) + 1;
    }

    private static string DecodeText(string base64)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(base64)); }
        catch (FormatException) { return string.Empty; }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "\n... [contenido truncado]";
}
