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
    // Cuantas veces, por turno, el sistema FUERZA verify_form al cierre y reinyecta los errores para que el
    // agente corrija. Acotado para no ciclar si el agente se empena en cerrar sin arreglar (el gate humano
    // ya impide aplicar el arreglo en el mismo turno; el flujo normal es: forzar 1 vez -> el agente PROPONE
    // la correccion -> se confirma -> al proximo cierre se re-verifica limpio).
    private const int MaxForcedVerify = 2;
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
                else if ((a.Mime?.Contains("html", StringComparison.OrdinalIgnoreCase) ?? false)
                    || a.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                    || a.FileName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                {
                    // HTML: es TEXTO; el agente lee la maqueta del formulario (secciones, campos, tablas) del
                    // marcado. Se pasa como contenido textual (no como imagen), acotado para no inflar el prompt.
                    var html = DecodeText(a.Base64);
                    if (!string.IsNullOrWhiteSpace(html))
                    {
                        sb.Append("\n\n[Contenido HTML del archivo ").Append(a.FileName)
                          .Append(" - deduce la estructura del formulario (secciones, campos y tablas) de este marcado]\n")
                          .Append(Truncate(html, 60000));
                    }
                }
                else if ((a.Mime?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ?? false)
                    || a.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    // Texto plano: se anexa tal cual (acotado).
                    var text = DecodeText(a.Base64);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.Append("\n\n[Contenido del archivo ").Append(a.FileName).Append("]\n").Append(Truncate(text, 60000));
                    }
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

        // Empujon automatico anti "narra pero no emite": si el agente responde SOLO texto con intencion de actuar
        // ("voy a agregar la tabla...") pero sin tool-calls, se le inyecta UNA vez este nudge transitorio (no se
        // guarda en el hilo, no lo ve el usuario) para que emita las llamadas, en vez de dejar la construccion a medias.
        string? transientNudge = null;
        var autoNudged = false;
        // Cuantas veces ya se forzo verify_form al cierre en este turno (tope MaxForcedVerify).
        var forcedVerifyRuns = 0;

        for (var round = 0; round < MaxRounds; round++)
        {
            var stored = await _store.GetMessagesAsync(conv.Id, cancellationToken);
            var messages = BuildProviderMessages(stored, images, docs);
            if (transientNudge is not null)
            {
                messages.Add(new AiToolMessage("user", transientNudge));
                transientNudge = null;
            }

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

            // Turno solo texto, sin herramientas.
            if (completion.ToolCalls is null || completion.ToolCalls.Count == 0)
            {
                await AddMessageAsync(conv, FormBuilderMessageRole.Assistant, completion.Text, null, cancellationToken);
                // Si el agente NARRO una accion futura ("voy a agregar...") pero no emitio nada, empujalo UNA vez a
                // ejecutar en vez de terminar el turno a medio construir.
                if (!autoNudged && StoppedMidAction(completion.Text))
                {
                    autoNudged = true;
                    transientNudge = "Describiste lo que ibas a hacer pero NO emitiste las llamadas a las herramientas. " +
                        "Emitelas AHORA (add_container/add_question/etc.) para eso que acabas de describir; no lo vuelvas a " +
                        "narrar. Si de verdad ya terminaste o necesitas que el usuario decida algo, dilo en una frase clara.";
                    continue;
                }

                // CIERRE: verify_form OBLIGATORIO, forzado por el SISTEMA (no se confia en que el modelo lo llame
                // ni en que diga la verdad al afirmar "ya verifique"). Si el turno cierra (texto sin herramientas y
                // no es una pregunta al usuario) y hay un formulario, se corre verify_form; si reporta errores, se
                // reinyectan al agente para que los corrija (el arreglo pasa por el gate humano). Acotado por
                // MaxForcedVerify para no ciclar si el agente insiste en cerrar sin arreglar.
                if (conv.FormDefinitionId is Guid verifyFormId && forcedVerifyRuns < MaxForcedVerify
                    && !EndsWithQuestion(completion.Text))
                {
                    var (verifyErrors, verifyDetail) = await RunMandatoryVerifyAsync(verifyFormId, actorUserId, cancellationToken);
                    if (verifyErrors > 0)
                    {
                        forcedVerifyRuns++;
                        transientNudge = "AUTO-VERIFICACION OBLIGATORIA DEL CIERRE (la corrio el SISTEMA, no tu memoria). " +
                            $"El formulario tiene {verifyErrors} error(es) de coherencia que DEBES corregir ANTES de cerrar. " +
                            "Emite YA las llamadas de correccion (no lo narres y NO afirmes que ya verificaste; el sistema " +
                            "re-verifica solo tras aplicar):\n" + verifyDetail;
                        continue;
                    }
                }
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

    // Heuristica para el empujon anti "narra pero no actua": el texto anuncia una ACCION futura de construccion
    // (voy a / procedo / a continuacion...) y NO esta esperando al usuario (no termina en pregunta ni dice que
    // ya termino). Conservadora: ante la duda NO empuja (y de todos modos solo empuja una vez por turno).
    internal static bool StoppedMidAction(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return false; }
        var t = text.Trim().ToLowerInvariant();
        if (t.EndsWith("?") || t.EndsWith("?\"") || t.Contains("?)")) { return false; } // esta preguntando algo
        // Senales de que YA termino o espera al usuario -> no empujar.
        if (t.Contains("puedes probar") || t.Contains("ya puedes") || t.Contains("he terminado")
            || t.Contains("esta listo") || t.Contains("está listo") || t.Contains("ya esta")
            || t.Contains("ya está") || t.Contains("hazmelo saber") || t.Contains("hazme saber")
            || t.Contains("necesito que") || t.Contains("confirma") || t.Contains("quieres que"))
        {
            return false;
        }
        // Intencion FUTURA de actuar (no pasado "he creado"): estas marcas denotan que iba a emitir tools.
        return t.Contains("voy a ") || t.Contains("vamos a ") || t.Contains("procedo a")
            || t.Contains("procedere") || t.Contains("procederé") || t.Contains("a continuacion")
            || t.Contains("a continuación") || t.Contains("enseguida") || t.Contains("acto seguido")
            || t.Contains("ahora agregare") || t.Contains("ahora agregaré") || t.Contains("ahora creare")
            || t.Contains("ahora anadire") || t.Contains("ahora añadiré") || t.Contains("aqui estan las llamadas")
            || t.Contains("aqui te presento las llamadas") || t.Contains("aquí te presento las llamadas");
    }

    // El texto es una PREGUNTA al usuario (espera respuesta) -> no es un cierre, no se fuerza la verificacion.
    internal static bool EndsWithQuestion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return false; }
        var t = text.TrimEnd();
        return t.EndsWith("?", StringComparison.Ordinal)
            || t.EndsWith("?\"", StringComparison.Ordinal) || t.EndsWith("?)", StringComparison.Ordinal)
            || t.EndsWith("?**", StringComparison.Ordinal);
    }

    // Corre verify_form (read-only, la MISMA tool del agente) sobre el formulario y devuelve el # de errores y
    // un detalle legible (solo severidad 'error') para reinyectar. Si algo falla, devuelve 0 errores para no
    // bloquear el cierre por un fallo del propio verificador.
    private async Task<(int Errors, string Detail)> RunMandatoryVerifyAsync(Guid formId, Guid actorUserId, CancellationToken ct)
    {
        try
        {
            var argsJson = JsonSerializer.Serialize(new { form_id = formId.ToString("D") }, Json);
            var r = await _toolset.ExecuteAsync("verify_form", argsJson, actorUserId, autonomous: true, ct);
            using var doc = JsonDocument.Parse(r.Json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return (0, string.Empty); }
            var errors = root.TryGetProperty("errors", out var e) && e.TryGetInt32(out var ec) ? ec : 0;
            if (errors <= 0) { return (0, string.Empty); }
            var sb = new StringBuilder();
            if (root.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in issues.EnumerateArray())
                {
                    if (it.ValueKind != JsonValueKind.Object) { continue; }
                    var sev = it.TryGetProperty("severity", out var sv) ? sv.GetString() : null;
                    if (!string.Equals(sev, "error", StringComparison.OrdinalIgnoreCase)) { continue; }
                    var where = it.TryGetProperty("where", out var w) ? w.GetString() : string.Empty;
                    var problem = it.TryGetProperty("problem", out var p) ? p.GetString() : string.Empty;
                    var fix = it.TryGetProperty("fix", out var f) ? f.GetString() : string.Empty;
                    sb.Append("- ").Append(where).Append(": ").Append(problem).Append(" -> ").Append(fix).Append('\n');
                }
            }
            return (errors, sb.ToString());
        }
        catch { return (0, string.Empty); }
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

    // Decodifica un adjunto de TEXTO (HTML/txt) desde base64 a string UTF-8. Devuelve "" si no es base64 valido.
    private static string DecodeText(string base64)
    {
        try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64)); }
        catch (FormatException) { return string.Empty; }
    }

    // Acota un texto largo (HTML puede ser enorme) para no inflar el prompt del modelo.
    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "\n... [contenido truncado]";
}
