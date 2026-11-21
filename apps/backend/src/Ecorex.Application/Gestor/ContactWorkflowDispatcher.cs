using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Scheduling;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ecorex.Application.Gestor;

/// <summary>
/// Motor de ejecucion del disenador de acciones por filtro de contactos (ADR-0056, Fase 2). Enganchado
/// al patron del <c>ScheduledJobWorker</c>/<c>ImportSchedulerWorker</c> (viven en Ecorex.SuperAdmin):
/// el worker barre cross-tenant SOLO ids, fija el tenant ambiente y llama aqui.
///
/// Por cada <see cref="ContactWorkflow"/> activo con una ventana vigente (ActiveDays + franja horaria en
/// la zona del tenant), resuelve el SEGMENTO del filtro (los <see cref="Tercero"/> que matchean sus
/// criterios, con el MISMO <see cref="ContactFilterEvaluator"/> que usa "Filtrar ahora") y, por cada paso,
/// ejecuta la accion sobre los contactos NO ejecutados aun ese dia (dedupe por <see cref="ContactWorkflowRun"/>),
/// acotando por <c>PackageSize</c> (tope por corrida) y <c>RepeatEvery</c> (minutos entre corridas).
///
/// Anti-spam por construccion: (1) una re-corrida en el mismo dia NO reenvia (indice unico de dedupe);
/// (2) cada corrida envia a lo sumo <c>PackageSize</c> contactos (o <see cref="DefaultBatchCap"/> si no se
/// configuro); (3) <c>RepeatEvery</c> espacia las corridas. Un fallo por contacto NO frena a los demas.
/// </summary>
public interface IContactWorkflowDispatcher
{
    /// <summary>
    /// Tenants con al menos un <see cref="ContactWorkflow"/> ACTIVO que tiene algun paso con ventana.
    /// UNICO punto cross-tenant (IgnoreQueryFilters); devuelve SOLO ids, ningun dato de negocio.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindTenantsWithActiveWorkflowsAsync(
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta los workflows del tenant ACTIVO (el llamador ya fijo el tenant ambiente). Devuelve cuantos
    /// registros de ejecucion (envios/skips) se escribieron. Cada paso/contacto va aislado.
    /// </summary>
    Task<int> RunDueForTenantAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ContactWorkflowDispatcher : IContactWorkflowDispatcher
{
    /// <summary>Tope de contactos por corrida cuando la ventana no define <c>PackageSize</c> (anti-spam).</summary>
    private const int DefaultBatchCap = 50;

    /// <summary>Techo duro por corrida aunque el usuario ponga un PackageSize enorme (defensa del proveedor).</summary>
    private const int HardBatchCap = 500;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IWhatsAppConnectorService _whatsapp;
    private readonly IEmailSender _email;
    private readonly ITaskItemService _tasks;
    private readonly Voice.IRetellVoiceService _voice;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ContactWorkflowDispatcher> _logger;
    // Bajas de correo + {enlace_baja} (ADR-0175). Opcional para no romper los dobles de prueba existentes.
    private readonly IEmailUnsubscribeService? _unsubscribes;
    private Dictionary<string, string>? _unsubscribedCache;

    public ContactWorkflowDispatcher(
        IApplicationDbContext db, ITenantContext tenant, IWhatsAppConnectorService whatsapp,
        IEmailSender email, ITaskItemService tasks, Voice.IRetellVoiceService voice, TimeProvider timeProvider,
        ILogger<ContactWorkflowDispatcher> logger, IEmailUnsubscribeService? unsubscribes = null)
    {
        _unsubscribes = unsubscribes;
        _db = db;
        _tenant = tenant;
        _whatsapp = whatsapp;
        _email = email;
        _tasks = tasks;
        _voice = voice;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Guid>> FindTenantsWithActiveWorkflowsAsync(
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
        // IgnoreQueryFilters: el worker aun no fijo tenant; barrido de PLATAFORMA (solo ids). Cada
        // ejecucion posterior va acotada al tenant dueno via AmbientTenantContext + filtro global.
        => await _db.ContactWorkflows.IgnoreQueryFilters()
            .Where(w => w.Activo && w.Steps.Any(s => s.Schedules.Any()))
            .Select(w => w.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<int> RunDueForTenantAsync(
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return 0;
        }
        _emailTemplateCache.Clear();
        _unsubscribedCache = null;

        var tz = ScheduledJobRecurrence.ResolveTimeZone(await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken));

        var localNow = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        var localTime = TimeOnly.FromDateTime(localNow.DateTime);
        var isoDay = ((int)localNow.DayOfWeek == 0) ? 7 : (int)localNow.DayOfWeek; // 1=lun..7=dom

        // Workflows activos con pasos + ventanas + el filtro (criterios) del tenant activo.
        var workflows = await _db.ContactWorkflows
            .Where(w => w.Activo)
            .Select(w => new
            {
                w.Id,
                w.TerceroFiltroId,
                Criterios = w.TerceroFiltro != null ? w.TerceroFiltro.CriteriosJson : null,
                Steps = w.Steps.OrderBy(s => s.Orden).Select(s => new
                {
                    s.Id,
                    s.StepType,
                    s.ParamsJson,
                    Schedules = s.Schedules.Select(h => new ScheduleRow(
                        h.Id, h.StartDate, h.EndDate, h.StartTime, h.EndTime, h.ActiveDays,
                        h.TemplateId, h.AccountId, h.RepeatEvery, h.PackageSize)).ToList()
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        if (workflows.Count == 0) { return 0; }

        var written = 0;
        foreach (var wf in workflows)
        {
            if (cancellationToken.IsCancellationRequested) { break; }

            // Ventanas vigentes AHORA en cualquiera de sus pasos: si no hay ninguna, no cargamos el segmento.
            var anyOpen = wf.Steps.Any(s => s.Schedules.Any(h => InWindow(h, localDate, localTime, isoDay)));
            if (!anyOpen) { continue; }

            var criterios = DeserializeCriterios(wf.Criterios);
            var segment = await ResolveSegmentAsync(criterios, cancellationToken);
            if (segment.Count == 0) { continue; }

            foreach (var step in wf.Steps)
            {
                foreach (var schedule in step.Schedules)
                {
                    if (cancellationToken.IsCancellationRequested) { break; }
                    if (!InWindow(schedule, localDate, localTime, isoDay)) { continue; }

                    // Rate: no volver a correr esta ventana antes de RepeatEvery minutos.
                    if (schedule.RepeatEvery is int every && every > 0)
                    {
                        var lastUtc = await _db.ContactWorkflowRuns
                            .Where(r => r.ContactWorkflowScheduleId == schedule.Id && r.WindowDate == localDate)
                            .Select(r => (DateTimeOffset?)r.DispatchedAtUtc)
                            .OrderByDescending(x => x)
                            .FirstOrDefaultAsync(cancellationToken);
                        if (lastUtc is DateTimeOffset last && (nowUtc - last) < TimeSpan.FromMinutes(every))
                        {
                            continue;
                        }
                    }

                    // Dedupe: contactos que YA tienen una fila para (paso, ventana, dia). Un Sent nunca se repite;
                    // un Failed/Skipped se REINTENTA sobre la misma fila segun ContactWorkflowRetryPolicy.
                    var existing = await _db.ContactWorkflowRuns
                        .Where(r => r.ContactWorkflowStepId == step.Id
                            && r.ContactWorkflowScheduleId == schedule.Id
                            && r.WindowDate == localDate)
                        .Select(r => new { r.Id, r.TerceroId, r.Status, r.Attempts, r.DispatchedAtUtc, r.Channel, r.ExternalRef })
                        .ToListAsync(cancellationToken);
                    var alreadySet = existing.Select(r => r.TerceroId).ToHashSet();
                    var retryIds = existing
                        .Where(r => ContactWorkflowRetryPolicy.ShouldRetry(r.Status, r.Attempts, r.DispatchedAtUtc, nowUtc))
                        .ToDictionary(r => r.TerceroId, r => r.Id);

                    // Llamada IA (ADR-0190): volver a llamar a quien no contesto / ocupado / buzon / rechazo, sobre la
                    // MISMA fila, si el paso lo pide y ya paso la espera. El run queda Enviado: se permite repetirlo.
                    var voiceRetryRuns = new HashSet<Guid>();
                    if (step.StepType == ContactWorkflowStepType.Llamada
                        && DeserializeCall(step.ParamsJson) is { Modo: "ia" } voiceCfg
                        && VoiceCallOutcomeRetries(voiceCfg) > 0)
                    {
                        var candidates = existing
                            .Where(r => r.Status == ContactWorkflowRunStatus.Sent && r.Channel == VoiceChannel
                                && !string.IsNullOrEmpty(r.ExternalRef) && !retryIds.ContainsKey(r.TerceroId))
                            .ToList();
                        if (candidates.Count > 0)
                        {
                            var refs = candidates.Select(r => r.ExternalRef!).ToList();
                            var outcomes = await _db.VoiceCalls.AsNoTracking()
                                .Where(v => refs.Contains(v.CallId))
                                .Select(v => new { v.CallId, v.Status, v.DisconnectionReason })
                                .ToDictionaryAsync(v => v.CallId, v => Voice.VoiceCallOutcomes.Classify(v.Status, v.DisconnectionReason), cancellationToken);
                            foreach (var r in candidates)
                            {
                                if (outcomes.TryGetValue(r.ExternalRef!, out var outcome)
                                    && Voice.VoiceCallOutcomes.ShouldRetry(outcome, r.Attempts, voiceCfg.Reintentos,
                                        voiceCfg.ReintentarCadaMin, r.DispatchedAtUtc, nowUtc))
                                {
                                    retryIds[r.TerceroId] = r.Id;
                                    voiceRetryRuns.Add(r.Id);
                                }
                            }
                        }
                    }

                    // Reintentos primero (son los mas antiguos), luego contactos nuevos; todo bajo el mismo tope.
                    var retries = segment.Where(c => retryIds.ContainsKey(c.Id)).ToList();
                    var pending = segment.Where(c => !alreadySet.Contains(c.Id)).ToList();
                    if (retries.Count == 0 && pending.Count == 0) { continue; }

                    var cap = schedule.PackageSize is int ps && ps > 0 ? Math.Min(ps, HardBatchCap) : DefaultBatchCap;
                    var retryBatch = retries.Take(cap).ToList();
                    var batch = pending.Take(cap - retryBatch.Count).ToList();

                    foreach (var contact in retryBatch)
                    {
                        if (cancellationToken.IsCancellationRequested) { break; }
                        var retryRunId = retryIds[contact.Id];
                        if (await RetryRunAsync(retryRunId, step.StepType, step.ParamsJson, schedule, contact, nowUtc,
                                voiceRetryRuns.Contains(retryRunId), cancellationToken))
                        {
                            written++;
                        }
                    }

                    var added = new List<ContactWorkflowRun>();
                    foreach (var contact in batch)
                    {
                        if (cancellationToken.IsCancellationRequested) { break; }
                        // Id asignado ANTES de ejecutar: viaja en el correo (ecx_run) para correlacionar los
                        // eventos de entrega del proveedor con esta fila (ADR-0188).
                        var runId = Guid.CreateVersion7();
                        var (status, channel, externalRef, error) =
                            await ExecuteStepAsync(step.StepType, step.ParamsJson, schedule, contact, runId, cancellationToken);

                        var run = new ContactWorkflowRun
                        {
                            Id = runId,
                            TenantId = tenantId,
                            ContactWorkflowStepId = step.Id,
                            ContactWorkflowScheduleId = schedule.Id,
                            TerceroId = contact.Id,
                            WindowDate = localDate,
                            DispatchedAtUtc = nowUtc,
                            Status = status,
                            Channel = channel,
                            ExternalRef = Truncate(externalRef, 200),
                            Error = Truncate(error, 600),
                            Attempts = 1
                        };
                        _db.ContactWorkflowRuns.Add(run);
                        added.Add(run);
                    }

                    if (added.Count == 0) { continue; }
                    try
                    {
                        await _db.SaveChangesAsync(cancellationToken);
                        written += added.Count;
                    }
                    catch (DbUpdateException)
                    {
                        // Choque contra el indice unico de dedupe: otra instancia del worker ya escribio
                        // parte de este lote. Idempotencia, no un error: se descarta y sigue. Se sueltan las
                        // filas del contexto para que no contaminen los SaveChanges siguientes de esta corrida.
                        foreach (var run in added)
                        {
                            _db.ContactWorkflowRuns.Entry(run).State = EntityState.Detached;
                        }
                        _logger.LogDebug(
                            "Lote de acciones del paso {StepId} (ventana {ScheduleId}) ya escrito por otra instancia.",
                            step.Id, schedule.Id);
                    }
                }
            }
        }

        return written;
    }

    /// <summary>
    /// Reintenta un Failed/Skipped sobre la MISMA fila. Primero la "reclama" (Attempts++ y nueva hora) con
    /// Attempts como token de concurrencia: si otra instancia del worker la tomo antes, el UPDATE no aplica y
    /// este proceso NO envia (sin dobles envios). Luego ejecuta el paso y guarda el resultado en la fila.
    /// <paramref name="allowSent"/> = reintento de una llamada que no contesto (ADR-0190): la fila esta Enviada.
    /// </summary>
    private async Task<bool> RetryRunAsync(
        Guid runId, ContactWorkflowStepType type, string? paramsJson, ScheduleRow schedule, SegmentContact contact,
        DateTimeOffset nowUtc, bool allowSent, CancellationToken cancellationToken)
    {
        var run = await _db.ContactWorkflowRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null || (run.Status == ContactWorkflowRunStatus.Sent && !allowSent)) { return false; }

        run.Attempts++;
        // Misma hora de la corrida que usan el rate-gate (RepeatEvery) y la regla de reintento.
        run.DispatchedAtUtc = nowUtc;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // DbUpdateConcurrencyException incluida: otra instancia ya reintento esta fila.
            _db.ContactWorkflowRuns.Entry(run).State = EntityState.Detached;
            return false;
        }

        var (status, channel, externalRef, error) =
            await ExecuteStepAsync(type, paramsJson, schedule, contact, run.Id, cancellationToken);
        run.Status = status;
        run.Channel = channel;
        run.ExternalRef = Truncate(externalRef, 200);
        run.Error = Truncate(error, 600);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex)
        {
            _db.ContactWorkflowRuns.Entry(run).State = EntityState.Detached;
            _logger.LogWarning(ex, "No se pudo guardar el reintento {RunId} del motor de acciones.", runId);
            return false;
        }
    }

    // ---- Ejecucion de las 5 acciones (mapeo a servicios existentes) ----

    private async Task<(ContactWorkflowRunStatus Status, string Channel, string? ExternalRef, string? Error)> ExecuteStepAsync(
        ContactWorkflowStepType type, string? paramsJson, ScheduleRow schedule, SegmentContact contact,
        Guid runId, CancellationToken cancellationToken)
    {
        var actor = _tenant.UserId ?? Guid.Empty;
        switch (type)
        {
            case ContactWorkflowStepType.Conectar:
                // Paso "no-envio": marca la entrada del contacto al workflow. Sin canal de mensajeria.
                return (ContactWorkflowRunStatus.Sent, "conectar", null, null);

            case ContactWorkflowStepType.WhatsApp:
                return await ExecuteWhatsAppAsync(paramsJson, schedule, contact, actor, cancellationToken);

            case ContactWorkflowStepType.Email:
                return await ExecuteEmailAsync(paramsJson, schedule, contact, runId, cancellationToken);

            case ContactWorkflowStepType.Llamada:
                return await ExecuteLlamadaAsync(paramsJson, contact, actor, runId, cancellationToken);

            case ContactWorkflowStepType.MensajeRed:
                // No hay canal de "responder redes" para INICIAR una conversacion saliente: el dispatcher
                // del agente solo RESPONDE mensajes entrantes. Se registra Skipped documentado (Fase 3).
                return (ContactWorkflowRunStatus.Skipped, "redes",
                    null, "Canal de redes no configurado para envio saliente (ver ADR-0056 Fase 3).");

            default:
                return (ContactWorkflowRunStatus.Skipped, "desconocido", null, $"Tipo de paso no soportado: {type}.");
        }
    }

    private async Task<(ContactWorkflowRunStatus, string, string?, string?)> ExecuteWhatsAppAsync(
        string? paramsJson, ScheduleRow schedule, SegmentContact contact, Guid actor, CancellationToken cancellationToken)
    {
        const string channel = "whatsapp";
        // Los terceros suelen guardarse en 10 digitos sin indicativo: E.164 con el pais por defecto (57).
        var phone = PhoneNumber.ToE164(contact.Telefono);
        if (phone is null)
        {
            return (ContactWorkflowRunStatus.Skipped, channel, null,
                string.IsNullOrWhiteSpace(contact.Telefono) ? "Contacto sin telefono." : "Telefono del contacto no valido.");
        }

        // Config del paso (ParamsJson); compatibilidad: ventanas viejas con texto en TemplateId y linea en AccountId.
        var wa = DeserializeWhatsApp(paramsJson);

        // Linea: la del paso, la de la ventana o la primera CONECTADA del tenant.
        var lineId = wa?.LineaId ?? schedule.AccountId;
        var line = lineId is Guid lid
            ? await _db.WhatsAppLines.Where(l => l.Id == lid).Select(l => new { l.Id, l.Status, l.Provider }).FirstOrDefaultAsync(cancellationToken)
            : await _db.WhatsAppLines.Where(l => l.Status == WhatsAppLineStatus.Connected)
                .Select(l => new { l.Id, l.Status, l.Provider }).FirstOrDefaultAsync(cancellationToken);
        if (line is null)
        {
            return (ContactWorkflowRunStatus.Skipped, channel, null,
                lineId is null ? "Sin linea WhatsApp conectada disponible." : "La linea WhatsApp del paso ya no existe.");
        }
        if (line.Status != WhatsAppLineStatus.Connected)
        {
            return (ContactWorkflowRunStatus.Skipped, channel, null, "La linea WhatsApp del paso no esta conectada.");
        }

        // YCloud/Cloud: fuera de la ventana de 24 h Meta solo acepta plantillas APROBADAS.
        if (line.Provider is WhatsAppProvider.YCloud or WhatsAppProvider.Cloud)
        {
            return await SendWhatsAppTemplateAsync(wa, line.Id, phone, contact, actor, cancellationToken);
        }

        var text = wa?.Texto ?? schedule.TemplateId;
        if (string.IsNullOrWhiteSpace(text))
        {
            return (ContactWorkflowRunStatus.Skipped, channel, null, "Paso WhatsApp sin mensaje configurado.");
        }
        var body = EmailTemplateService.RenderTemplate(text.Trim(), MergeFieldsFor(contact), htmlEscapeValues: false);

        // remoteJid guardado del contacto (soporta LID, ADR-0055): de una conversacion previa por telefono,
        // buscando por las formas equivalentes del numero (con y sin indicativo).
        var forms = PhoneNumber.LookupForms(phone).ToList();
        var remoteJid = await _db.Conversations
            .Where(c => c.ContactPhone != null && forms.Contains(c.ContactPhone) && c.RemoteJid != null)
            .Select(c => c.RemoteJid)
            .FirstOrDefaultAsync(cancellationToken);

        var result = await _whatsapp.SendTestAsync(line.Id, phone, body, actor, remoteJid, cancellationToken);
        return result.Ok
            ? (ContactWorkflowRunStatus.Sent, channel, result.MessageId, null)
            : (ContactWorkflowRunStatus.Failed, channel, null, result.Error ?? "Fallo el envio de WhatsApp.");
    }

    private async Task<(ContactWorkflowRunStatus, string, string?, string?)> SendWhatsAppTemplateAsync(
        ContactWorkflowWhatsAppParams? wa, Guid lineId, string phone, SegmentContact contact, Guid actor,
        CancellationToken cancellationToken)
    {
        const string channel = "whatsapp";
        if (wa?.PlantillaId is not Guid tplId)
        {
            return (ContactWorkflowRunStatus.Skipped, channel, null,
                "La linea es YCloud/Cloud: el paso necesita una plantilla aprobada (Meta rechaza texto libre fuera de 24 h).");
        }
        var tpl = await _db.WhatsAppTemplates.AsNoTracking()
            .Where(t => t.Id == tplId)
            .Select(t => new { t.Name, t.Language, t.Status, t.IsActive, t.VariablesJson, t.HeaderType, t.HeaderMediaUrl })
            .FirstOrDefaultAsync(cancellationToken);
        if (tpl is null || !tpl.IsActive)
        {
            return (ContactWorkflowRunStatus.Skipped, channel, null, "La plantilla de WhatsApp del paso no existe o esta inactiva.");
        }
        if (tpl.Status != WhatsAppTemplateStatus.Approved)
        {
            return (ContactWorkflowRunStatus.Skipped, channel, null, $"La plantilla '{tpl.Name}' no esta aprobada por Meta ({tpl.Status}).");
        }

        // Una entrada por variable de la plantilla, en su orden; Meta rechaza parametros vacios.
        var values = new List<string>();
        foreach (var token in TemplateTokens(tpl.VariablesJson))
        {
            var source = wa.Variables is not null && wa.Variables.TryGetValue(token, out var mapped)
                ? mapped
                : ContactWorkflowWhatsAppFields.DefaultFor(token);
            var value = ContactWorkflowWhatsAppFields.Resolve(source, contact.Nombre, contact.Empresa, contact.Cargo,
                contact.Ciudad, contact.Email, phone);
            if (value is null)
            {
                return (ContactWorkflowRunStatus.Skipped, channel, null,
                    $"La variable '{token}' de la plantilla queda vacia para este contacto.");
            }
            values.Add(value);
        }

        // Encabezado de imagen/documento/video (ADR-0186): Meta exige la media en CADA envio, no solo al aprobar.
        var (headerType, headerUrl) = HeaderMediaFor(tpl.HeaderType, tpl.HeaderMediaUrl);
        if (WhatsAppTemplateComponents.MediaHeaderFormat(tpl.HeaderType) is not null && headerUrl is null)
        {
            return (ContactWorkflowRunStatus.Skipped, channel, null,
                $"La plantilla '{tpl.Name}' tiene encabezado de {tpl.HeaderType} pero no tiene archivo guardado; subelo en Plantillas WhatsApp.");
        }
        var result = await _whatsapp.SendTemplateAsync(lineId, phone, tpl.Name, tpl.Language, values, actor,
            headerType, headerUrl, cancellationToken: cancellationToken);
        return result.Ok
            ? (ContactWorkflowRunStatus.Sent, channel, result.MessageId, null)
            : (ContactWorkflowRunStatus.Failed, channel, null, result.Error ?? "Fallo el envio de la plantilla de WhatsApp.");
    }

    /// <summary>Tipo ("image"/"document"/"video") y URL del encabezado de media de la plantilla, o (null, null).</summary>
    public static (string? Type, string? Url) HeaderMediaFor(WhatsAppTemplateHeaderType? headerType, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) { return (null, null); }
        return headerType switch
        {
            WhatsAppTemplateHeaderType.Image => ("image", url.Trim()),
            WhatsAppTemplateHeaderType.Document => ("document", url.Trim()),
            WhatsAppTemplateHeaderType.Video => ("video", url.Trim()),
            _ => (null, null)
        };
    }

    /// <summary>Tokens de VariablesJson ([{ "Token": "cliente", ... }]) en orden; la caja de la propiedad no importa.</summary>
    internal static List<string> TemplateTokens(string? variablesJson)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(variablesJson)) { return tokens; }
        try
        {
            using var doc = JsonDocument.Parse(variablesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return tokens; }
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) { continue; }
                foreach (var prop in el.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "token", StringComparison.OrdinalIgnoreCase)
                        && prop.Value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                    {
                        tokens.Add(prop.Value.GetString()!.Trim());
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Variables mal formadas: la plantilla se envia sin parametros (el proveedor dira si faltan).
        }
        return tokens;
    }

    private async Task<(ContactWorkflowRunStatus, string, string?, string?)> ExecuteEmailAsync(
        string? paramsJson, ScheduleRow schedule, SegmentContact contact, Guid runId, CancellationToken cancellationToken)
    {
        const string channel = "email";
        var to = contact.Email?.Trim();
        if (string.IsNullOrWhiteSpace(to))
        {
            return (ContactWorkflowRunStatus.Skipped, channel, null, "Contacto sin correo.");
        }

        // La ventana referencia una EmailTemplate (su Id como texto). Sin plantilla o inactiva -> Skipped.
        if (!Guid.TryParse(schedule.TemplateId, out var templateId))
        {
            return (ContactWorkflowRunStatus.Skipped, channel, null, "Ventana de E-mail sin plantilla seleccionada.");
        }
        var tpl = await GetEmailTemplateAsync(templateId, cancellationToken);
        if (tpl is null)
        {
            return (ContactWorkflowRunStatus.Skipped, channel, null, "La plantilla de correo no existe o esta inactiva.");
        }

        // Baja de correos (ADR-0175): quien abrio su {enlace_baja} no vuelve a recibir correos del motor. Tambien
        // los suprimidos por el proveedor: rebote definitivo, spam o baja en SendGrid (ADR-0188).
        if (await SuppressionOriginAsync(to, cancellationToken) is { } origen)
        {
            return (ContactWorkflowRunStatus.Skipped, channel, null, SendGridEvents.SkipReasonFor(origen));
        }

        // Merge de las variables con los datos del contacto: asunto en texto plano, cuerpo en HTML (escapa
        // las variables para evitar inyeccion). {empresa} usa el nombre de la empresa padre (o Sector).
        var fields = MergeFieldsFor(contact);
        var subject = EmailTemplateService.RenderTemplate(tpl.Asunto, fields, htmlEscapeValues: false);
        if (string.IsNullOrWhiteSpace(subject)) { subject = tpl.Nombre; }
        var htmlBody = EmailTemplateService.RenderTemplate(tpl.CuerpoHtml, fields, htmlEscapeValues: true);

        // Remitente comercial de la campana (ADR-0185); null = el del servidor de correo configurado.
        // Lleva los ids de correlacion para los eventos de entrega del proveedor (ADR-0188).
        var sender = WithTracking(EmailSenderFor(paramsJson), _tenant.TenantId, runId);
        var result = await _email.SendAsync(to, subject, htmlBody, sender, cancellationToken);
        return result.Ok
            ? (ContactWorkflowRunStatus.Sent, channel, result.MessageId, null)
            : (ContactWorkflowRunStatus.Failed, channel, null, result.Error ?? "Fallo el envio de correo.");
    }

    // Carga (una vez por corrida) la plantilla de correo ACTIVA del tenant. Cache por Id: una ventana
    // envia el mismo template a todo su lote, evitando una consulta por contacto.
    private readonly Dictionary<Guid, EmailTemplateRow?> _emailTemplateCache = new();

    private async Task<EmailTemplateRow?> GetEmailTemplateAsync(Guid templateId, CancellationToken cancellationToken)
    {
        if (_emailTemplateCache.TryGetValue(templateId, out var cached)) { return cached; }
        var tpl = await _db.EmailTemplates.AsNoTracking()
            .Where(t => t.Id == templateId && t.Activa)
            .Select(t => new EmailTemplateRow(t.Nombre, t.Asunto, t.CuerpoHtml))
            .FirstOrDefaultAsync(cancellationToken);
        _emailTemplateCache[templateId] = tpl;
        return tpl;
    }

    private sealed record EmailTemplateRow(string Nombre, string Asunto, string CuerpoHtml);

    // Valores de merge del contacto (ADR-0175 suma vendedor/telefono/sector/firma/enlace_baja).
    private EmailMergeFields MergeFieldsFor(SegmentContact contact)
    {
        string? enlace = null;
        if (_unsubscribes is not null && _tenant.TenantId is Guid tid && !string.IsNullOrWhiteSpace(contact.Email))
        {
            enlace = _unsubscribes.BuildLink(tid, contact.Email!);
        }
        return new EmailMergeFields(contact.Nombre, contact.Empresa, contact.Cargo, contact.Ciudad, contact.Email,
            Vendedor: contact.Vendedor, Telefono: contact.Telefono, Sector: contact.Sector,
            Firma: contact.Firma, EnlaceBaja: enlace);
    }

    /// <summary>Origen de la baja del correo (enlace, manual, rebote, spam, proveedor) o null si puede recibir.</summary>
    private async Task<string?> SuppressionOriginAsync(string email, CancellationToken cancellationToken)
    {
        if (_unsubscribes is null) { return null; }
        _unsubscribedCache ??= await _unsubscribes.GetSuppressionsAsync(cancellationToken);
        return _unsubscribedCache.TryGetValue(EmailUnsubscribeService.Normalize(email), out var origen) ? origen : null;
    }

    /// <summary>
    /// Remitente del envio con los argumentos de correlacion (ecx_tenant, ecx_run) que el proveedor devuelve en
    /// sus eventos de entrega (ADR-0188). Sin remitente propio se crea uno vacio (usa el del servidor).
    /// </summary>
    public static EmailSenderOverride WithTracking(EmailSenderOverride? sender, Guid? tenantId, Guid runId)
    {
        var args = new Dictionary<string, string>(StringComparer.Ordinal) { [SendGridEvents.RunArg] = runId.ToString() };
        if (tenantId is Guid tid) { args[SendGridEvents.TenantArg] = tid.ToString(); }
        return (sender ?? new EmailSenderOverride(null, null, null)) with { CustomArgs = args };
    }

    /// <summary>Canal de los runs de Llamada IA (su ExternalRef es el call_id de Retell).</summary>
    private const string VoiceChannel = "voz-ia";

    private static int VoiceCallOutcomeRetries(ContactWorkflowCallParams call)
        => Voice.VoiceCallOutcomes.ClampRetries(call.Reintentos);

    private async Task<(ContactWorkflowRunStatus, string, string?, string?)> ExecuteLlamadaAsync(
        string? paramsJson, SegmentContact contact, Guid actor, Guid runId, CancellationToken cancellationToken)
    {
        const string channel = "crm";
        var call = DeserializeCall(paramsJson);
        // Llamada IA (ADR-0056): coloca la llamada con Retell reutilizando el AiAgent (su SystemPrompt +
        // PromptExtra + objetivo componen el prompt que REEMPLAZA el del agente Retell). El dedupe del
        // ContactWorkflowRun evita re-llamar; el unico reintento es el configurado por "no contesto" (ADR-0190).
        if (call?.Modo == "ia")
        {
            const string voiceChannel = VoiceChannel;
            var e164 = PhoneNumber.ToE164(contact.Telefono);
            if (!Voice.RetellVoiceService.IsE164(e164))
            {
                return (ContactWorkflowRunStatus.Skipped, voiceChannel, null, "Contacto sin telefono valido (E.164).");
            }
            var contactVars = new Dictionary<string, string?>
            {
                ["nombre"] = contact.Nombre,
                ["empresa"] = contact.Empresa,
                ["cargo"] = contact.Cargo,
                ["ciudad"] = contact.Ciudad
            };
            var voiceRes = await _voice.PlaceCallAsync(new Voice.VoicePlaceCallRequest(
                ToNumber: e164!,
                AiAgentId: call.AgenteId,
                PromptExtra: call.PromptExtra,
                Objetivo: call.Objetivo,
                FormulariosPermitidos: call.FormulariosPermitidos ?? Array.Empty<Guid>(),
                ContactVariables: contactVars,
                ContactWorkflowRunId: runId,
                RetellVoiceLineId: call.LineaVozId), cancellationToken);

            return voiceRes.Placed
                ? (ContactWorkflowRunStatus.Sent, voiceChannel, voiceRes.CallId, null)
                : (ContactWorkflowRunStatus.Failed, voiceChannel, null, voiceRes.Error ?? "No se pudo colocar la llamada IA.");
        }
        if (call is null || !Guid.TryParse(call.Subcategoria, out var subcategoriaId))
        {
            // Categoria/Subcategoria mapean al concepto 000270 (SubcategoriaId); sin uno valido no hay
            // puente Concepto->Tarea, no se crea gestion.
            return (ContactWorkflowRunStatus.Skipped, channel, null,
                "Paso Llamada sin concepto (subcategoria) valido en sus parametros.");
        }

        Guid? assignee = Guid.TryParse(call.Comercial, out var comercialId) ? comercialId : null;
        var priority = Enum.TryParse<TaskPriority>(call.Prioridad, ignoreCase: true, out var p) ? p : TaskPriority.Medium;

        var request = new CreateTaskItemRequest(
            Title: $"Gestion: {contact.Nombre}",
            ActivityTypeId: null,
            Priority: priority,
            AssigneeTenantUserId: assignee,
            RequesterName: contact.Nombre,
            RequesterEmail: contact.Email,
            RequesterPhone: contact.Telefono,
            SubcategoriaId: subcategoriaId); // puente Concepto -> Tarea

        var result = await _tasks.CreateAsync(request, actor, "Motor de acciones (ADR-0056)", cancellationToken);
        if (!result.IsOk || result.Value is null)
        {
            return (ContactWorkflowRunStatus.Failed, channel, null,
                $"No se pudo crear la gestion CRM: {result.Error}");
        }
        return (ContactWorkflowRunStatus.Sent, channel, result.Value.Item.Number, null);
    }

    // ---- Segmento del filtro ----

    private async Task<List<SegmentContact>> ResolveSegmentAsync(
        IReadOnlyList<FiltroCriterio> criterios, CancellationToken cancellationToken)
    {
        // Mismo universo que el conteo del Gestor: terceros no inactivos. Se evalua en memoria con el
        // evaluador COMPARTIDO (una sola logica de filtrado). Se trae ademas Empresa/Cargo/Ciudad para el
        // merge del correo ({empresa}={nombre de la empresa padre, o Sector}, {cargo}, {ciudad}).
        var rows = await _db.Terceros.AsNoTracking()
            .Where(t => t.Estado != TerceroEstado.Inactivo)
            .Select(t => new
            {
                t.Id,
                t.Nombre,
                t.Ciudad,
                t.Vendedor,
                t.Sector,
                t.Cargo,
                t.Perfiles,
                t.Estado,
                t.Email,
                t.Telefono,
                EmpresaNombre = t.Empresa != null ? t.Empresa.Nombre : null,
                // {vendedor}/{firma} (ADR-0175): asesor vinculado (o el texto legado) y la firma de su usuario.
                AsesorNombre = t.VendedorAsesor != null ? t.VendedorAsesor.Nombre : null,
                AsesorFirma = t.VendedorAsesor != null && t.VendedorAsesor.TenantUser != null
                    ? t.VendedorAsesor.TenantUser.FirmaHtml : null
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(t => ContactFilterEvaluator.MatchesAll(
                new ContactFilterEvaluator.Row(t.Nombre, t.Ciudad, t.Vendedor, t.Sector, t.Cargo, t.Perfiles, t.Estado),
                criterios))
            .Select(t => new SegmentContact(
                t.Id, t.Nombre, t.Email, t.Telefono,
                string.IsNullOrWhiteSpace(t.EmpresaNombre) ? t.Sector : t.EmpresaNombre,
                t.Cargo, t.Ciudad,
                string.IsNullOrWhiteSpace(t.AsesorNombre) ? t.Vendedor : t.AsesorNombre,
                t.Sector, t.AsesorFirma))
            .ToList();
    }

    // ---- Ventana / horario ----

    private static bool InWindow(ScheduleRow h, DateOnly localDate, TimeOnly localTime, int isoDay)
    {
        if (h.StartDate is DateOnly sd && localDate < sd) { return false; }
        if (h.EndDate is DateOnly ed && localDate > ed) { return false; }

        if (!ParseDays(h.ActiveDays).Contains(isoDay)) { return false; }

        // Franja normal (inicio <= fin) o nocturna (inicio > fin, cruza medianoche).
        return h.StartTime <= h.EndTime
            ? localTime >= h.StartTime && localTime <= h.EndTime
            : localTime >= h.StartTime || localTime <= h.EndTime;
    }

    private static HashSet<int> ParseDays(string? csv)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(csv)) { return set; }
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var d) && d is >= 1 and <= 7) { set.Add(d); }
        }
        return set;
    }

    // ---- Helpers ----

    private static IReadOnlyList<FiltroCriterio> DeserializeCriterios(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<FiltroCriterio>(); }
        try
        {
            return JsonSerializer.Deserialize<List<FiltroCriterio>>(json, JsonOpts) ?? new List<FiltroCriterio>();
        }
        catch (JsonException)
        {
            return Array.Empty<FiltroCriterio>();
        }
    }

    public static EmailSenderOverride? EmailSenderFor(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson)) { return null; }
        try
        {
            var p = JsonSerializer.Deserialize<ContactWorkflowEmailParams>(paramsJson, JsonOpts);
            return p is null || (string.IsNullOrWhiteSpace(p.RemitenteNombre) && string.IsNullOrWhiteSpace(p.RemitenteCorreo)
                && string.IsNullOrWhiteSpace(p.ResponderA))
                ? null
                : new EmailSenderOverride(p.RemitenteNombre, p.RemitenteCorreo, p.ResponderA);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ContactWorkflowWhatsAppParams? DeserializeWhatsApp(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try
        {
            return JsonSerializer.Deserialize<ContactWorkflowWhatsAppParams>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ContactWorkflowCallParams? DeserializeCall(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try
        {
            return JsonSerializer.Deserialize<ContactWorkflowCallParams>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Truncate(string? value, int max)
        => value is null ? null : value.Length <= max ? value : value[..max];

    private sealed record ScheduleRow(
        Guid Id, DateOnly? StartDate, DateOnly? EndDate, TimeOnly StartTime, TimeOnly EndTime,
        string ActiveDays, string? TemplateId, Guid? AccountId, int? RepeatEvery, int? PackageSize);

    private sealed record SegmentContact(
        Guid Id, string Nombre, string? Email, string? Telefono,
        string? Empresa, string? Cargo, string? Ciudad,
        string? Vendedor = null, string? Sector = null, string? Firma = null);
}
