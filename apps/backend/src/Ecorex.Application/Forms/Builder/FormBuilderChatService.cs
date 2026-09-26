using System.Text;
using System.Text.Json;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms.Builder;

/// <summary>
/// Implementacion del asistente conversacional de creacion de formularios. Reusa el cliente del AI Gateway
/// (IAiProviderClient.CompleteWithToolsAsync) y las herramientas de FormAuthoringToolset, pero con un GATE
/// HUMANO propio: cuando el modelo pide ejecutar herramientas MUTANTES, no se corren solas; se persisten
/// como PROPUESTAS (estado Pending) y se devuelven a la UI. Solo tras ConfirmAsync se ejecutan y se continua
/// el bucle. Las herramientas de SOLO LECTURA (descubrimiento) se ejecutan sin gate. Proveedor: Gemini.
/// </summary>
public sealed class FormBuilderChatService : IFormBuilderChatService
{
    private readonly ISecretProtector _secrets;
    private readonly IAiProviderClient _ai;
    private readonly IFormAuthoringToolset _toolset;
    private readonly IFormBuilderChatStore _store;
    private readonly IFormSnapshotService _snapshots;

    // Proveedor fijo para esta funcion (decision de producto): Gemini (fuerte en tool-use + vision + PDF nativo).
    private const AiProvider Provider = AiProvider.Gemini;
    // Modelo por defecto de la funcion: FLASH (respuesta rapida en un chat con muchas herramientas; pro es
    // demasiado lento para el ida y vuelta interactivo). Es un modelo valido de Gemini en el catalogo.
    private const string FeatureModel = "gemini-2.5-flash";
    // Tope de vueltas del bucle (llamadas al modelo) por turno: evita ciclos si el modelo insiste con lecturas.
    private const int MaxRounds = 8;
    // Timeout por llamada al modelo: un cuelgue debe fallar limpio en vez de congelar la conversacion.
    private static readonly TimeSpan AiCallTimeout = TimeSpan.FromSeconds(90);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public FormBuilderChatService(
        ISecretProtector secrets, IAiProviderClient ai,
        IFormAuthoringToolset toolset, IFormBuilderChatStore store,
        IFormSnapshotService snapshots)
    {
        _secrets = secrets;
        _ai = ai;
        _toolset = toolset;
        _store = store;
        _snapshots = snapshots;
    }

    public async Task<FormBuilderStartResult> StartAsync(Guid? formDefinitionId, Guid actorTenantUserId, CancellationToken cancellationToken = default)
    {
        // Valida que el proveedor este habilitado antes de crear la conversacion.
        var cfg = await _store.ResolveProviderAsync(Provider, cancellationToken);
        if (cfg is null || !cfg.Enabled || string.IsNullOrWhiteSpace(cfg.ApiKeyEncrypted))
        {
            return new FormBuilderStartResult(false, $"El proveedor de IA {Provider} no esta habilitado en la plataforma.", Guid.Empty, null);
        }
        var model = FeatureModel;

        string title = "Nuevo formulario";
        if (formDefinitionId is Guid fid)
        {
            var t = await _store.GetFormTitleAsync(fid, cancellationToken);
            if (!string.IsNullOrWhiteSpace(t)) { title = t!; }
        }

        var conv = await _store.CreateConversationAsync(formDefinitionId, Provider, model, title, actorTenantUserId, cancellationToken);
        return new FormBuilderStartResult(true, null, conv.Id, conv.FormDefinitionId);
    }

    public async Task<FormBuilderTurnResult> SendAsync(Guid conversationId, string? userText, IReadOnlyList<FormBuilderAttachment>? attachments, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var conv = await _store.GetConversationAsync(conversationId, cancellationToken);
        if (conv is null) { return FormBuilderTurnResult.Fail(conversationId, "La conversacion no existe."); }

        // Procesa adjuntos: Excel -> texto que se anexa al mensaje; imagen/PDF -> inline para vision.
        var images = new List<AiInlineImage>();
        var docs = new List<AiInlineDocument>();
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(userText)) { sb.Append(userText!.Trim()); }
        var attMeta = new List<object>();
        if (attachments is not null)
        {
            foreach (var a in attachments)
            {
                if (a is null || string.IsNullOrWhiteSpace(a.Base64)) { continue; }
                attMeta.Add(new { a.FileName, a.Mime });
                if (SpreadsheetText.IsSpreadsheet(a.Mime, a.FileName))
                {
                    var text = SpreadsheetText.FromBase64(a.Base64, a.FileName);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.Append("\n\n[Contenido del archivo ").Append(a.FileName).Append("]\n").Append(text);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(a.Mime) && a.Mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    images.Add(new AiInlineImage(a.Base64, a.Mime));
                }
                else if ((a.Mime?.Contains("pdf", StringComparison.OrdinalIgnoreCase) ?? false)
                    || a.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    docs.Add(new AiInlineDocument(a.Base64, string.IsNullOrWhiteSpace(a.Mime) ? "application/pdf" : a.Mime, a.FileName));
                }
            }
        }

        var content = sb.ToString();
        if (string.IsNullOrWhiteSpace(content) && images.Count == 0 && docs.Count == 0)
        {
            return FormBuilderTurnResult.Fail(conversationId, "Escribe un mensaje o adjunta un archivo.");
        }

        await AddMessageAsync(conv, FormBuilderMessageRole.User, content,
            attMeta.Count > 0 ? JsonSerializer.Serialize(attMeta, Json) : null, cancellationToken);

        return await RunAgentAsync(conv, images, docs, actorUserId, cancellationToken);
    }

    public async Task<FormBuilderTurnResult> ConfirmAsync(Guid conversationId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var conv = await _store.GetConversationAsync(conversationId, cancellationToken);
        if (conv is null) { return FormBuilderTurnResult.Fail(conversationId, "La conversacion no existe."); }

        var msgs = await _store.GetMessagesAsync(conversationId, cancellationToken);
        var pending = msgs.Where(m => m.Role == FormBuilderMessageRole.Proposal && m.ProposalState == FormBuilderProposalState.Pending)
            .OrderBy(m => m.Sequence).ToList();
        if (pending.Count == 0) { return FormBuilderTurnResult.Fail(conversationId, "No hay acciones pendientes por confirmar."); }

        // VERSIONADO: si ya hay un formulario, toma un snapshot ANTES de aplicar el lote confirmado, para
        // poder revertir si el agente lo daña. Best-effort: un fallo del snapshot NO bloquea la ejecucion.
        if (conv.FormDefinitionId is Guid snapFormId)
        {
            try
            {
                var tools = string.Join(", ", pending.Select(p => p.ToolName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());
                var label = $"Antes de: {(string.IsNullOrWhiteSpace(tools) ? "cambios del asistente" : tools)}";
                await _snapshots.SnapshotAsync(snapFormId, label, FormSnapshotTrigger.BeforeAgentBatch, conversationId, actorUserId, cancellationToken);
            }
            catch { /* el snapshot es una red de seguridad; nunca frena la operacion */ }
        }

        // Ejecuta cada herramienta propuesta EN ORDEN; guarda el resultado y marca Confirmed.
        foreach (var p in pending)
        {
            string resultJson;
            try
            {
                var r = await _toolset.ExecuteAsync(p.ToolName ?? string.Empty, p.ToolArgsJson ?? "{}", actorUserId, autonomous: true, cancellationToken);
                resultJson = r.Json;
            }
            catch (Exception ex)
            {
                resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message }, Json);
            }
            p.ProposalState = FormBuilderProposalState.Confirmed;
            p.ToolResultJson = resultJson;
            await _store.SaveMessageAsync(p, cancellationToken);

            // Si se creo el formulario, liga la conversacion a su id (para editar en vivo).
            if (conv.FormDefinitionId is null && string.Equals(p.ToolName, "create_form", StringComparison.OrdinalIgnoreCase)
                && TryExtractFormId(resultJson) is Guid newId)
            {
                conv.FormDefinitionId = newId;
                await _store.SaveConversationAsync(conv, cancellationToken);
            }
        }

        return await RunAgentAsync(conv, images: null, docs: null, actorUserId, cancellationToken);
    }

    public async Task<FormBuilderTurnResult> RejectAsync(Guid conversationId, string? reason, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var conv = await _store.GetConversationAsync(conversationId, cancellationToken);
        if (conv is null) { return FormBuilderTurnResult.Fail(conversationId, "La conversacion no existe."); }

        var msgs = await _store.GetMessagesAsync(conversationId, cancellationToken);
        var pending = msgs.Where(m => m.Role == FormBuilderMessageRole.Proposal && m.ProposalState == FormBuilderProposalState.Pending)
            .OrderBy(m => m.Sequence).ToList();
        if (pending.Count == 0) { return FormBuilderTurnResult.Fail(conversationId, "No hay acciones pendientes por rechazar."); }

        foreach (var p in pending)
        {
            p.ProposalState = FormBuilderProposalState.Rejected;
            await _store.SaveMessageAsync(p, cancellationToken);
        }

        // Deja constancia del rechazo para que el modelo lo tenga en cuenta en el siguiente turno.
        var note = string.IsNullOrWhiteSpace(reason)
            ? "El usuario rechazo la accion propuesta. Propon una alternativa o pregunta que ajustar."
            : $"El usuario rechazo la accion propuesta. Motivo: {reason}. Propon una alternativa o pregunta que ajustar.";
        await AddMessageAsync(conv, FormBuilderMessageRole.User, note, null, cancellationToken);

        return await RunAgentAsync(conv, images: null, docs: null, actorUserId, cancellationToken);
    }

    public async Task<IReadOnlyList<FormBuilderMessage>> GetTranscriptAsync(Guid conversationId, CancellationToken cancellationToken = default)
        => await _store.GetMessagesAsync(conversationId, cancellationToken);

    // ===== Nucleo: bucle del agente con GATE humano =====
    private async Task<FormBuilderTurnResult> RunAgentAsync(FormBuilderConversation conv, IReadOnlyList<AiInlineImage>? images, IReadOnlyList<AiInlineDocument>? docs, Guid actorUserId, CancellationToken cancellationToken)
    {
        var cfg = await _store.ResolveProviderAsync(Provider, cancellationToken);
        if (cfg is null || !cfg.Enabled || string.IsNullOrWhiteSpace(cfg.ApiKeyEncrypted))
        {
            return FormBuilderTurnResult.Fail(conv.Id, $"El proveedor de IA {Provider} no esta habilitado en la plataforma.");
        }
        string apiKey;
        try { apiKey = _secrets.Unprotect(cfg.ApiKeyEncrypted!); }
        catch { return FormBuilderTurnResult.Fail(conv.Id, "La API key del proveedor esta cifrada con una version anterior. Vuelve a guardarla en Servidores de IA."); }

        var meta = AiProviderCatalog.For(Provider);
        var model = !string.IsNullOrWhiteSpace(conv.Model) ? conv.Model! : FeatureModel;
        var baseUrl = !string.IsNullOrWhiteSpace(cfg.BaseUrl) ? cfg.BaseUrl : meta.DefaultBaseUrl;

        var tenantName = await _store.GetTenantNameAsync(conv.TenantId, cancellationToken);
        if (string.IsNullOrWhiteSpace(tenantName)) { tenantName = "tu empresa"; }
        var systemPrompt = FormBuilderHarness.SystemPrompt(tenantName, editingExisting: conv.FormDefinitionId is not null,
            formId: conv.FormDefinitionId?.ToString("D"));
        var tools = _toolset.GetSpecs();
        var readOnly = _toolset.ReadOnlyTools;

        for (var round = 0; round < MaxRounds; round++)
        {
            var stored = await _store.GetMessagesAsync(conv.Id, cancellationToken);
            var messages = BuildProviderMessages(stored, images, docs);

            AiCompletion completion;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(AiCallTimeout);
                completion = await _ai.CompleteWithToolsAsync(Provider, apiKey, baseUrl, model, systemPrompt, messages, tools, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return FormBuilderTurnResult.Fail(conv.Id, "La IA tardo demasiado en responder. Intenta de nuevo o simplifica la instruccion.");
            }
            catch (Exception ex)
            {
                return FormBuilderTurnResult.Fail(conv.Id, "Error hablando con la IA: " + ex.Message);
            }
            if (!completion.Ok)
            {
                return FormBuilderTurnResult.Fail(conv.Id, completion.Error ?? "La IA no respondio.");
            }

            // Turno FINAL: solo texto, sin herramientas.
            if (completion.ToolCalls is null || completion.ToolCalls.Count == 0)
            {
                await AddMessageAsync(conv, FormBuilderMessageRole.Assistant, completion.Text, null, cancellationToken);
                return new FormBuilderTurnResult(true, null, conv.Id, conv.FormDefinitionId, completion.Text,
                    Array.Empty<FormBuilderProposalDto>(), AwaitingConfirmation: false);
            }

            // Hay tool-calls. Si TODAS son de solo lectura, se ejecutan sin gate y se sigue el bucle.
            var allReadOnly = completion.ToolCalls.All(tc => readOnly.Contains(tc.Name));
            if (allReadOnly)
            {
                if (!string.IsNullOrWhiteSpace(completion.Text))
                {
                    await AddMessageAsync(conv, FormBuilderMessageRole.Assistant, completion.Text, null, cancellationToken);
                }
                foreach (var tc in completion.ToolCalls)
                {
                    string resultJson;
                    try
                    {
                        var r = await _toolset.ExecuteAsync(tc.Name, tc.ArgumentsJson, actorUserId, autonomous: true, cancellationToken);
                        resultJson = r.Json;
                    }
                    catch (Exception ex)
                    {
                        resultJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message }, Json);
                    }
                    // Persistimos la lectura como una propuesta AUTO-confirmada (para reconstruir el hilo con el proveedor).
                    await AddProposalAsync(conv, tc, FormBuilderProposalState.Confirmed, resultJson, cancellationToken);
                }
                // Las imagenes/documentos ya viajaron en esta ronda; no reinyectar en las siguientes.
                images = null; docs = null;
                continue;
            }

            // Hay al menos una accion MUTANTE: se PROPONE todo el turno y se detiene el bucle (gate humano).
            if (!string.IsNullOrWhiteSpace(completion.Text))
            {
                await AddMessageAsync(conv, FormBuilderMessageRole.Assistant, completion.Text, null, cancellationToken);
            }
            var proposals = new List<FormBuilderProposalDto>();
            foreach (var tc in completion.ToolCalls)
            {
                var m = await AddProposalAsync(conv, tc, FormBuilderProposalState.Pending, null, cancellationToken);
                proposals.Add(new FormBuilderProposalDto(m.Id, tc.Name, tc.ArgumentsJson));
            }
            return new FormBuilderTurnResult(true, null, conv.Id, conv.FormDefinitionId, completion.Text, proposals, AwaitingConfirmation: true);
        }

        return FormBuilderTurnResult.Fail(conv.Id, "El asistente hizo demasiadas consultas seguidas. Intenta de nuevo o precisa la instruccion.");
    }

    // Reconstruye la lista de mensajes para el proveedor a partir de los mensajes guardados.
    private static List<AiToolMessage> BuildProviderMessages(IReadOnlyList<FormBuilderMessage> stored, IReadOnlyList<AiInlineImage>? images, IReadOnlyList<AiInlineDocument>? docs)
    {
        var ordered = stored.OrderBy(m => m.Sequence).ToList();
        var outMsgs = new List<AiToolMessage>();
        var i = 0;
        while (i < ordered.Count)
        {
            var m = ordered[i];
            switch (m.Role)
            {
                case FormBuilderMessageRole.User:
                    outMsgs.Add(new AiToolMessage("user", m.Content ?? string.Empty));
                    i++;
                    break;
                case FormBuilderMessageRole.Assistant:
                    outMsgs.Add(new AiToolMessage("assistant", m.Content ?? string.Empty));
                    i++;
                    break;
                case FormBuilderMessageRole.Proposal when m.ProposalState == FormBuilderProposalState.Confirmed:
                    {
                        // Agrupa la CORRIDA contigua de propuestas confirmadas en un turno assistant + sus resultados tool.
                        var run = new List<FormBuilderMessage>();
                        while (i < ordered.Count && ordered[i].Role == FormBuilderMessageRole.Proposal
                            && ordered[i].ProposalState == FormBuilderProposalState.Confirmed)
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
                    // Propuestas Pending/Rejected (no ejecutadas) no forman parte del historial del proveedor.
                    i++;
                    break;
            }
        }

        // Anexa las imagenes/documentos del turno actual al ULTIMO mensaje de usuario (vision/PDF nativo).
        if ((images is { Count: > 0 } || docs is { Count: > 0 }))
        {
            for (var k = outMsgs.Count - 1; k >= 0; k--)
            {
                if (outMsgs[k].Role == "user")
                {
                    outMsgs[k] = outMsgs[k] with { Images = images, Documents = docs };
                    break;
                }
            }
        }
        return outMsgs;
    }

    private async Task<FormBuilderMessage> AddMessageAsync(FormBuilderConversation conv, FormBuilderMessageRole role, string? content, string? attachmentsJson, CancellationToken ct)
    {
        var seq = await NextSequenceAsync(conv.Id, ct);
        return await _store.AddMessageAsync(new FormBuilderMessage
        {
            ConversationId = conv.Id,
            Sequence = seq,
            Role = role,
            Content = content,
            AttachmentsJson = attachmentsJson,
        }, ct);
    }

    private async Task<FormBuilderMessage> AddProposalAsync(FormBuilderConversation conv, AiToolCall tc, FormBuilderProposalState state, string? resultJson, CancellationToken ct)
    {
        var seq = await NextSequenceAsync(conv.Id, ct);
        return await _store.AddMessageAsync(new FormBuilderMessage
        {
            ConversationId = conv.Id,
            Sequence = seq,
            Role = FormBuilderMessageRole.Proposal,
            ToolCallId = tc.Id,
            ToolName = tc.Name,
            ToolArgsJson = tc.ArgumentsJson,
            ToolResultJson = resultJson,
            ProposalState = state,
        }, ct);
    }

    private async Task<int> NextSequenceAsync(Guid conversationId, CancellationToken ct)
    {
        var existing = await _store.GetMessagesAsync(conversationId, ct);
        return existing.Count == 0 ? 1 : existing.Max(m => m.Sequence) + 1;
    }

    // Extrae el id del formulario recien creado del JSON de resultado de create_form (best-effort).
    private static Guid? TryExtractFormId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var key in new[] { "id", "formId", "form_id", "definitionId", "definition_id" })
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty(key, out var v)
                    && v.ValueKind == JsonValueKind.String
                    && Guid.TryParse(v.GetString(), out var g))
                {
                    return g;
                }
            }
        }
        catch (JsonException) { }
        return null;
    }
}
