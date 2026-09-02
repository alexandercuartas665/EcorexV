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

    public ContactWorkflowDispatcher(
        IApplicationDbContext db, ITenantContext tenant, IWhatsAppConnectorService whatsapp,
        IEmailSender email, ITaskItemService tasks, Voice.IRetellVoiceService voice, TimeProvider timeProvider,
        ILogger<ContactWorkflowDispatcher> logger)
    {
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

                    // Dedupe: contactos que YA tienen una fila para (paso, ventana, dia).
                    var already = await _db.ContactWorkflowRuns
                        .Where(r => r.ContactWorkflowStepId == step.Id
                            && r.ContactWorkflowScheduleId == schedule.Id
                            && r.WindowDate == localDate)
                        .Select(r => r.TerceroId)
                        .ToListAsync(cancellationToken);
                    var alreadySet = already.ToHashSet();

                    var pending = segment.Where(c => !alreadySet.Contains(c.Id)).ToList();
                    if (pending.Count == 0) { continue; }

                    var cap = schedule.PackageSize is int ps && ps > 0 ? Math.Min(ps, HardBatchCap) : DefaultBatchCap;
                    var batch = pending.Take(cap).ToList();

                    var batchWritten = 0;
                    foreach (var contact in batch)
                    {
                        if (cancellationToken.IsCancellationRequested) { break; }
                        var (status, channel, externalRef, error) =
                            await ExecuteStepAsync(step.StepType, step.ParamsJson, schedule, contact, cancellationToken);

                        _db.ContactWorkflowRuns.Add(new ContactWorkflowRun
                        {
                            TenantId = tenantId,
                            ContactWorkflowStepId = step.Id,
                            ContactWorkflowScheduleId = schedule.Id,
                            TerceroId = contact.Id,
                            WindowDate = localDate,
                            DispatchedAtUtc = _timeProvider.GetUtcNow(),
                            Status = status,
                            Channel = channel,
                            ExternalRef = Truncate(externalRef, 200),
                            Error = Truncate(error, 600)
                        });
                        batchWritten++;
                    }

                    if (batchWritten == 0) { continue; }
                    try
                    {
                        await _db.SaveChangesAsync(cancellationToken);
                        written += batchWritten;
                    }
                    catch (DbUpdateException)
                    {
                        // Choque contra el indice unico de dedupe: otra instancia del worker ya escribio
                        // parte de este lote. Idempotencia, no un error: se descarta y sigue.
                        _logger.LogDebug(
                            "Lote de acciones del paso {StepId} (ventana {ScheduleId}) ya escrito por otra instancia.",
                            step.Id, schedule.Id);
                    }
                }
            }
        }

        return written;
    }

    // ---- Ejecucion de las 5 acciones (mapeo a servicios existentes) ----

    private async Task<(ContactWorkflowRunStatus Status, string Channel, string? ExternalRef, string? Error)> ExecuteStepAsync(
        ContactWorkflowStepType type, string? paramsJson, ScheduleRow schedule, SegmentContact contact,
        CancellationToken cancellationToken)
    {
        var actor = _tenant.UserId ?? Guid.Empty;
        switch (type)
        {
            case ContactWorkflowStepType.Conectar:
                // Paso "no-envio": marca la entrada del contacto al workflow. Sin canal de mensajeria.
                return (ContactWorkflowRunStatus.Sent, "conectar", null, null);

            case ContactWorkflowStepType.WhatsApp:
                return await ExecuteWhatsAppAsync(schedule, contact, actor, cancellationToken);

            case ContactWorkflowStepType.Email:
                return await ExecuteEmailAsync(schedule, contact, cancellationToken);

            case ContactWorkflowStepType.Llamada:
                return await ExecuteLlamadaAsync(paramsJson, contact, actor, cancellationToken);

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
        ScheduleRow schedule, SegmentContact contact, Guid actor, CancellationToken cancellationToken)
    {
        const string channel = "whatsapp";
        var phone = contact.Telefono?.Trim();
        if (string.IsNullOrWhiteSpace(phone))
        {
            return (ContactWorkflowRunStatus.Skipped, channel, null, "Contacto sin telefono.");
        }
        var text = schedule.TemplateId?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            // Fase 3 traera plantillas reales; por ahora TemplateId se usa como texto libre del mensaje.
            return (ContactWorkflowRunStatus.Skipped, channel, null, "Ventana sin mensaje/plantilla configurada.");
        }

        // Linea: la de la ventana (AccountId), o la primera CONECTADA del tenant.
        var lineId = schedule.AccountId;
        if (lineId is null)
        {
            lineId = await _db.WhatsAppLines
                .Where(l => l.Status == WhatsAppLineStatus.Connected)
                .Select(l => (Guid?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (lineId is not Guid line)
        {
            return (ContactWorkflowRunStatus.Skipped, channel, null, "Sin linea WhatsApp conectada disponible.");
        }

        // remoteJid guardado del contacto (soporta LID, ADR-0055): de una conversacion previa por telefono.
        var remoteJid = await _db.Conversations
            .Where(c => c.ContactPhone == phone && c.RemoteJid != null)
            .Select(c => c.RemoteJid)
            .FirstOrDefaultAsync(cancellationToken);

        var result = await _whatsapp.SendTestAsync(line, phone, text, actor, remoteJid, cancellationToken);
        return result.Ok
            ? (ContactWorkflowRunStatus.Sent, channel, result.MessageId, null)
            : (ContactWorkflowRunStatus.Failed, channel, null, result.Error ?? "Fallo el envio de WhatsApp.");
    }

    private async Task<(ContactWorkflowRunStatus, string, string?, string?)> ExecuteEmailAsync(
        ScheduleRow schedule, SegmentContact contact, CancellationToken cancellationToken)
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

        // Merge de las variables con los datos del contacto: asunto en texto plano, cuerpo en HTML (escapa
        // las variables para evitar inyeccion). {empresa} usa el nombre de la empresa padre (o Sector).
        var fields = new EmailMergeFields(contact.Nombre, contact.Empresa, contact.Cargo, contact.Ciudad, contact.Email);
        var subject = EmailTemplateService.RenderTemplate(tpl.Asunto, fields, htmlEscapeValues: false);
        if (string.IsNullOrWhiteSpace(subject)) { subject = tpl.Nombre; }
        var htmlBody = EmailTemplateService.RenderTemplate(tpl.CuerpoHtml, fields, htmlEscapeValues: true);

        var result = await _email.SendAsync(to, subject, htmlBody, cancellationToken);
        return result.Ok
            ? (ContactWorkflowRunStatus.Sent, channel, null, null)
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

    private async Task<(ContactWorkflowRunStatus, string, string?, string?)> ExecuteLlamadaAsync(
        string? paramsJson, SegmentContact contact, Guid actor, CancellationToken cancellationToken)
    {
        const string channel = "crm";
        var call = DeserializeCall(paramsJson);
        // Llamada IA (ADR-0056): coloca la llamada con Retell reutilizando el AiAgent (su SystemPrompt +
        // PromptExtra + objetivo componen el prompt que REEMPLAZA el del agente Retell). El dedupe del
        // ContactWorkflowRun evita re-llamar; nunca se reintenta a ciegas.
        if (call?.Modo == "ia")
        {
            const string voiceChannel = "voz-ia";
            if (!Voice.RetellVoiceService.IsE164(contact.Telefono))
            {
                return (ContactWorkflowRunStatus.Skipped, voiceChannel, null, "Contacto sin telefono en formato E.164.");
            }
            var contactVars = new Dictionary<string, string?>
            {
                ["nombre"] = contact.Nombre,
                ["empresa"] = contact.Empresa,
                ["cargo"] = contact.Cargo,
                ["ciudad"] = contact.Ciudad
            };
            var voiceRes = await _voice.PlaceCallAsync(new Voice.VoicePlaceCallRequest(
                ToNumber: contact.Telefono!,
                AiAgentId: call.AgenteId,
                PromptExtra: call.PromptExtra,
                Objetivo: call.Objetivo,
                FormulariosPermitidos: call.FormulariosPermitidos ?? Array.Empty<Guid>(),
                ContactVariables: contactVars), cancellationToken);

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
                t.Id, t.Nombre, t.Ciudad, t.Vendedor, t.Sector, t.Cargo, t.Perfiles, t.Estado,
                t.Email, t.Telefono,
                EmpresaNombre = t.Empresa != null ? t.Empresa.Nombre : null
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(t => ContactFilterEvaluator.MatchesAll(
                new ContactFilterEvaluator.Row(t.Nombre, t.Ciudad, t.Vendedor, t.Sector, t.Cargo, t.Perfiles, t.Estado),
                criterios))
            .Select(t => new SegmentContact(
                t.Id, t.Nombre, t.Email, t.Telefono,
                string.IsNullOrWhiteSpace(t.EmpresaNombre) ? t.Sector : t.EmpresaNombre,
                t.Cargo, t.Ciudad))
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
        string? Empresa, string? Cargo, string? Ciudad);
}
