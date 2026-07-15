using Ecorex.Application.Common;
using Ecorex.Application.Notifications;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ecorex.Application.Scheduling;

/// <summary>
/// Runner del Motor de programaciones (modulo 000889, ola P2; doc D5). Sucesor de TaskProgrammer /
/// sweb_taskProgramer.asmx del origen, que nunca cerro el bucle de ejecucion ni el dispatch de canales.
///
/// Contrato del worker: IDEMPOTENTE (una ventana = un disparo), aislado por tenant y OBSERVABLE (cada
/// disparo deja una fila en <c>scheduled_job_runs</c>, que es la fuente de los KPIs "ejecutados hoy" y
/// "errores" que el origen dejaba en 0).
/// </summary>
public interface IScheduledJobDispatcher
{
    /// <summary>
    /// Tenants con al menos una regla VENCIDA de una programacion ACTIVA. Es el UNICO punto cross-tenant
    /// del motor (barrido de plataforma con IgnoreQueryFilters); la ejecucion posterior ya va acotada al
    /// tenant dueno del job.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindTenantsWithDueRulesAsync(
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispara las reglas vencidas del tenant ACTIVO (el llamador ya fijo el tenant ambiente). Devuelve
    /// cuantas ventanas se dispararon. Cada regla se procesa aislada: un fallo no tumba a las demas.
    /// </summary>
    Task<int> RunDueForTenantAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ScheduledJobDispatcher : IScheduledJobDispatcher
{
    /// <summary>Intentos totales sobre una ventana antes de darla por perdida (dead-letter). Ola P4.</summary>
    private const int MaxAttempts = 3;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly INotificationService _notifications;
    private readonly ITaskItemService _tasks;
    private readonly IEnumerable<IScheduledJobChannelSender> _channelSenders;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScheduledJobDispatcher> _logger;

    public ScheduledJobDispatcher(
        IApplicationDbContext db, ITenantContext tenantContext,
        INotificationService notifications, ITaskItemService tasks,
        IEnumerable<IScheduledJobChannelSender> channelSenders, TimeProvider timeProvider,
        ILogger<ScheduledJobDispatcher> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _notifications = notifications;
        _tasks = tasks;
        _channelSenders = channelSenders;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Espera antes del reintento N (backoff): 5, 15 minutos...</summary>
    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMinutes(5 * attempt);

    public async Task<IReadOnlyList<Guid>> FindTenantsWithDueRulesAsync(
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
        // IgnoreQueryFilters: el worker no tiene tenant fijado todavia; este barrido es de PLATAFORMA
        // (solo devuelve ids de tenant, ningun dato de negocio) y cada ejecucion posterior va acotada.
        //
        // Incluye tambien las reglas SIN PROGRAMAR (NextRunAt == null): son reglas que quedarian MUERTAS
        // (nunca dispararian) si el calculo no se hizo al guardarlas -por ejemplo, programaciones creadas
        // antes de que existiera el motor-. Al visitarlas, RunDueForTenantAsync las reprograma.
        => await _db.ScheduledJobRules.IgnoreQueryFilters()
            .Where(r => r.NextRunAt == null || r.NextRunAt <= nowUtc)
            .Join(_db.ScheduledJobs.IgnoreQueryFilters().Where(j => j.Status == ScheduledJobStatus.Active),
                r => r.JobId, j => j.Id, (r, j) => r.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<int> RunDueForTenantAsync(
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return 0;
        }

        var tz = ScheduledJobRecurrence.ResolveTimeZone(await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken));

        // AUTO-REPARACION: reglas de programaciones ACTIVAS que quedaron sin programar (NextRunAt null)
        // nunca dispararian. Se les calcula la proxima ventana y se dejan listas para el siguiente ciclo.
        var unscheduled = await _db.ScheduledJobRules
            .Where(r => r.NextRunAt == null)
            .Join(_db.ScheduledJobs.Where(j => j.Status == ScheduledJobStatus.Active),
                r => r.JobId, j => j.Id, (r, j) => r)
            .ToListAsync(cancellationToken);
        if (unscheduled.Count > 0)
        {
            foreach (var rule in unscheduled)
            {
                rule.NextRunAt = ScheduledJobRecurrence.ComputeNextRun(rule, nowUtc, tz);
            }
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Motor de programaciones: {Count} regla(s) sin programar reprogramadas en el tenant {TenantId}.",
                unscheduled.Count, tenantId);
        }

        // Solo reglas vencidas de programaciones ACTIVAS (las Pausadas NO disparan).
        var due = await _db.ScheduledJobRules
            .Where(r => r.NextRunAt != null && r.NextRunAt <= nowUtc)
            .Join(_db.ScheduledJobs.Where(j => j.Status == ScheduledJobStatus.Active),
                r => r.JobId, j => j.Id, (r, j) => new { Rule = r, Job = j })
            .OrderBy(x => x.Rule.NextRunAt)
            .Take(200) // techo por pasada: evita que un backlog gigante monopolice el ciclo
            .ToListAsync(cancellationToken);

        var fired = 0;
        foreach (var item in due)
        {
            if (cancellationToken.IsCancellationRequested) { break; }
            if (await FireAsync(item.Job, item.Rule, tz, cancellationToken))
            {
                fired++;
            }
        }
        return fired;
    }

    /// <summary>
    /// Dispara UNA ventana: ejecuta la accion, escribe la bitacora y avanza NextRunAt, todo en la misma
    /// transaccion. La ventana disparada es <c>rule.NextRunAt</c> (NO "ahora"): asi el disparo es
    /// reproducible y el indice unico (tenant, job, rule, fired_at) garantiza que no se repita.
    /// </summary>
    private async Task<bool> FireAsync(
        ScheduledJob job, ScheduledJobRule rule, TimeZoneInfo tz, CancellationToken cancellationToken)
    {
        if (rule.NextRunAt is not DateTimeOffset scheduledAt)
        {
            return false;
        }

        // La VENTANA conserva su identidad durante los reintentos: si hay una ventana fallida pendiente,
        // se sigue reintentando ESA (NextRunAt es solo el instante del reintento, no una ventana nueva).
        var window = rule.PendingWindowAt ?? scheduledAt;
        var attempt = rule.Attempt + 1;

        var result = ScheduledJobRunResult.Ok;
        string? detail;
        string? createdRef = null;

        try
        {
            (result, detail, createdRef) = await ExecuteAsync(job, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo al disparar la programacion {Code} (regla {RuleId})", job.Code, rule.Id);
            result = ScheduledJobRunResult.Error;
            detail = Truncate(ex.Message, 600);
        }

        var nowUtc = _timeProvider.GetUtcNow();
        var failed = result == ScheduledJobRunResult.Error;
        var deadLettered = failed && attempt >= MaxAttempts;

        if (failed && !deadLettered)
        {
            // REINTENTO (ola P4): se conserva la ventana y se reprograma con backoff.
            rule.PendingWindowAt = window;
            rule.Attempt = attempt;
            rule.NextRunAt = nowUtc + Backoff(attempt);
            detail = Truncate($"{detail} [intento {attempt}/{MaxAttempts}; se reintenta en {Backoff(attempt).TotalMinutes:0} min]", 600);
        }
        else
        {
            // Exito, o DEAD-LETTER tras agotar los intentos: en ambos casos la ventana se cierra y la
            // regla vuelve a su cadencia normal (la ventana perdida queda registrada como Error).
            if (deadLettered)
            {
                detail = Truncate($"{detail} [descartada tras {attempt} intentos: dead-letter]", 600);
            }
            rule.PendingWindowAt = null;
            rule.Attempt = 0;
            // La proxima ejecucion se calcula DESDE la ventana (no desde "ahora"): un worker atrasado no
            // se salta ventanas intermedias.
            rule.NextRunAt = ScheduledJobRecurrence.ComputeNextRun(rule, window, tz);
        }

        _db.ScheduledJobRuns.Add(new ScheduledJobRun
        {
            JobId = job.Id,
            RuleId = rule.Id,
            FiredAt = window,
            Attempt = attempt,
            Result = result,
            Detail = detail,
            CreatedEntityRef = createdRef,
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Choque contra el indice unico (tenant, job, rule, fired_at, attempt): otra instancia del
            // worker ya disparo esta MISMA ventana en este MISMO intento. Idempotencia, no un error.
            _logger.LogDebug("Ventana {Window:o} (intento {Attempt}) de {Code} ya disparada por otra instancia.",
                window, attempt, job.Code);
            return false;
        }
    }

    /// <summary>Accion segun el tipo de programacion.</summary>
    private async Task<(ScheduledJobRunResult Result, string? Detail, string? CreatedRef)> ExecuteAsync(
        ScheduledJob job, CancellationToken cancellationToken)
    {
        switch (job.Type)
        {
            case ScheduledJobType.Notification:
                return await NotifyAsync(job, cancellationToken);

            case ScheduledJobType.Activity:
                return await CreateActivityAsync(job, cancellationToken);

            default:
                return (ScheduledJobRunResult.Skipped, $"Tipo no soportado: {job.Type}.", null);
        }
    }

    /// <summary>
    /// Entrega la notificacion (ola P4): in-app al encargado + los CANALES configurados, de verdad. La
    /// bitacora dice canal por canal que paso; si algun canal falla, la ejecucion es un Error y entra al
    /// ciclo de reintento (antes se reportaba Ok listando los canales sin haber enviado nada por ellos).
    /// </summary>
    private async Task<(ScheduledJobRunResult Result, string? Detail, string? CreatedRef)> NotifyAsync(
        ScheduledJob job, CancellationToken cancellationToken)
    {
        var title = job.Name;
        var body = $"Recordatorio programado ({job.Code}).";
        var lines = new List<string>();
        var anyFailure = false;

        // 1) In-app al encargado.
        if (job.AssigneeTenantUserId is Guid recipientId)
        {
            await _notifications.CreateAsync(
                recipientId, NotificationKind.General, title, body,
                linkRoute: "/programar-actividad", cancellationToken: cancellationToken);
            lines.Add("In-app: entregada.");
        }
        else
        {
            lines.Add("In-app: sin encargado, no hay destinatario.");
        }

        // 2) Canales externos configurados.
        var channels = await _db.ScheduledJobChannels
            .Where(c => c.JobId == job.Id)
            .Select(c => c.Channel)
            .ToListAsync(cancellationToken);

        if (channels.Count > 0)
        {
            var recipient = await ResolveRecipientAsync(job.AssigneeTenantUserId, cancellationToken);
            foreach (var channel in channels)
            {
                var sender = _channelSenders.FirstOrDefault(s => s.Channel == channel);
                if (sender is null)
                {
                    // Canal SIN integracion (Slack/SMS): no se finge la entrega.
                    lines.Add($"{channel}: canal sin integracion, no se envio.");
                    continue;
                }
                if (recipient is null)
                {
                    lines.Add($"{channel}: sin encargado, no hay a quien enviar.");
                    anyFailure = true;
                    continue;
                }
                var sent = await sender.SendAsync(recipient, title, body, cancellationToken);
                lines.Add(sent.Detail);
                if (!sent.Delivered) { anyFailure = true; }
            }
        }

        var detail = Truncate(string.Join(" ", lines), 600);
        return (anyFailure ? ScheduledJobRunResult.Error : ScheduledJobRunResult.Ok, detail, null);
    }

    /// <summary>
    /// Resuelve el destinatario: el correo del encargado y su numero de WhatsApp, que en este modelo es el
    /// <c>PhoneNumber</c> de la linea que tiene asignada (no hay telefono en el perfil del usuario).
    /// </summary>
    private async Task<ScheduledJobRecipient?> ResolveRecipientAsync(
        Guid? assigneeId, CancellationToken cancellationToken)
    {
        if (assigneeId is not Guid id) { return null; }

        var email = await _db.TenantUsers
            .Where(u => u.Id == id)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        if (email is null) { return null; }

        var phone = await _db.WhatsAppLines
            .Where(l => l.AssignedToTenantUserId == id && l.PhoneNumber != null)
            .Select(l => l.PhoneNumber)
            .FirstOrDefaultAsync(cancellationToken);

        return new ScheduledJobRecipient(id, email, phone);
    }

    /// <summary>
    /// Crea la ACTIVIDAD (ola P3). "Tarea" y "actividad" son LA MISMA entidad (TaskItem): la que produce
    /// el wizard de 4 pasos. Por eso aqui NO se duplica nada: se llama al MISMO
    /// <see cref="ITaskItemService.CreateAsync"/> pasando el <c>SubcategoriaId</c> del concepto, que es
    /// quien dispara el puente Concepto->Tarea dentro del servicio (titulo automatico, tablero del
    /// concepto, arranque del flujo y aviso a los destinatarios del concepto).
    /// </summary>
    private async Task<(ScheduledJobRunResult Result, string? Detail, string? CreatedRef)> CreateActivityAsync(
        ScheduledJob job, CancellationToken cancellationToken)
    {
        if (job.SubcategoryId is not Guid subcategoriaId)
        {
            return (ScheduledJobRunResult.Error,
                "La programacion es de tipo Actividad pero no tiene concepto (sub-categoria) configurado.", null);
        }

        // Titulo: manda el TituloAuto del concepto (soporta tokens tipo @cliente). Si el concepto no lo
        // define, TaskItemService rechazaria la tarea sin titulo -> se cae al nombre de la programacion.
        var tituloAuto = await _db.ActividadSubcategorias
            .Where(s => s.Id == subcategoriaId)
            .Select(s => s.TituloAuto)
            .FirstOrDefaultAsync(cancellationToken);
        var title = string.IsNullOrWhiteSpace(tituloAuto) ? job.Name : "";

        var request = new CreateTaskItemRequest(
            Title: title,
            ActivityTypeId: null,
            SubcategoriaId: subcategoriaId,              // el CONCEPTO: puente Concepto -> Tarea
            AssigneeTenantUserId: job.AssigneeTenantUserId, // encargado OPCIONAL (null -> nace Pendiente)
            EntidadId: job.AreaEntityId);

        // Actor: quien configuro la programacion (si se conoce); el nombre deja rastro de que fue el motor.
        var actor = job.CreatedBy ?? Guid.Empty;
        var result = await _tasks.CreateAsync(
            request, actor, $"Motor de programaciones ({job.Code})", cancellationToken);

        if (!result.IsOk || result.Value is null)
        {
            return (ScheduledJobRunResult.Error,
                Truncate($"No se pudo crear la actividad: {result.Error}", 600), null);
        }

        var task = result.Value.Item;
        return (ScheduledJobRunResult.Ok,
            $"Actividad creada: {task.Number} - {task.Title}.",
            task.Number);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
