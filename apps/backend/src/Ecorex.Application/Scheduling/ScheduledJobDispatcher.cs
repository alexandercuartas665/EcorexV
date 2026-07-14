using Ecorex.Application.Common;
using Ecorex.Application.Notifications;
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
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly INotificationService _notifications;
    private readonly ILogger<ScheduledJobDispatcher> _logger;

    public ScheduledJobDispatcher(
        IApplicationDbContext db, ITenantContext tenantContext,
        INotificationService notifications, ILogger<ScheduledJobDispatcher> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _notifications = notifications;
        _logger = logger;
    }

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
        if (rule.NextRunAt is not DateTimeOffset window)
        {
            return false;
        }

        var result = ScheduledJobRunResult.Ok;
        string? detail;
        string? createdRef = null;

        try
        {
            (result, detail, createdRef) = await ExecuteAsync(job, cancellationToken);
        }
        catch (Exception ex)
        {
            // Un fallo de la accion NO frena el motor: queda registrado como Error y la regla igual avanza
            // (el reintento con dead-letter es un incremento posterior, ola P4).
            _logger.LogError(ex, "Fallo al disparar la programacion {Code} (regla {RuleId})", job.Code, rule.Id);
            result = ScheduledJobRunResult.Error;
            detail = Truncate(ex.Message, 600);
        }

        _db.ScheduledJobRuns.Add(new ScheduledJobRun
        {
            JobId = job.Id,
            RuleId = rule.Id,
            FiredAt = window,
            Result = result,
            Detail = detail,
            CreatedEntityRef = createdRef,
        });

        // Avanza la ventana: la proxima ejecucion se calcula DESDE la ventana disparada (no desde "ahora"),
        // de modo que un worker atrasado no se salte ventanas intermedias.
        rule.NextRunAt = ScheduledJobRecurrence.ComputeNextRun(rule, window, tz);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Choque contra el indice unico (tenant, job, rule, fired_at): otra instancia del worker ya
            // disparo esta MISMA ventana. Es el caso feliz de la idempotencia, no un error.
            _logger.LogDebug("Ventana {Window:o} de {Code} ya disparada por otra instancia; se descarta.",
                window, job.Code);
            return false;
        }
    }

    /// <summary>Accion segun el tipo de programacion.</summary>
    private async Task<(ScheduledJobRunResult Result, string? Detail, string? CreatedRef)> ExecuteAsync(
        ScheduledJob job, CancellationToken cancellationToken)
    {
        var channels = await _db.ScheduledJobChannels
            .Where(c => c.JobId == job.Id)
            .Select(c => c.Channel)
            .ToListAsync(cancellationToken);
        var channelList = channels.Count == 0 ? "(sin canales)" : string.Join(", ", channels);

        switch (job.Type)
        {
            case ScheduledJobType.Notification:
                if (job.AssigneeTenantUserId is not Guid recipient)
                {
                    // Sin encargado no hay destinatario in-app; los canales externos llegan en P4.
                    return (ScheduledJobRunResult.Ok,
                        $"Sin encargado: no hay destinatario in-app. Canales configurados: {channelList}.", null);
                }
                await _notifications.CreateAsync(
                    recipient,
                    NotificationKind.General,
                    title: job.Name,
                    body: $"Recordatorio programado ({job.Code}).",
                    linkRoute: "/programar-actividad",
                    cancellationToken: cancellationToken);
                return (ScheduledJobRunResult.Ok,
                    $"Notificacion in-app entregada. Canales configurados: {channelList}.", null);

            case ScheduledJobType.Activity:
                // La creacion de la ACTIVIDAD (== TaskItem, la misma del wizard de 4 pasos) reutiliza
                // ITaskItemService.CreateAsync con el SubcategoriaId del concepto: se construye en la ola P3.
                return (ScheduledJobRunResult.Skipped,
                    "Tipo Actividad: la creacion de la tarea desde el concepto llega en la ola P3.", null);

            default:
                return (ScheduledJobRunResult.Skipped, $"Tipo no soportado: {job.Type}.", null);
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
