using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.Scheduling;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Dispara las programaciones de importacion VENCIDAS de un tenant. Separado del worker para poder
/// probarlo sin levantar un BackgroundService.
/// </summary>
public interface IImportScheduleDispatcher
{
    /// <summary>Barrido de PLATAFORMA: que tenants tienen algo vencido. Devuelve solo ids.</summary>
    Task<IReadOnlyList<Guid>> FindTenantsWithDueProcessesAsync(DateTimeOffset nowUtc, CancellationToken ct = default);

    /// <summary>Dispara lo vencido del tenant que este fijado en el contexto. Devuelve cuantas disparo.</summary>
    Task<int> RunDueForTenantAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
}

public sealed class ImportScheduleDispatcher(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IProcessRunner runner,
    ILogger<ImportScheduleDispatcher> log) : IImportScheduleDispatcher
{
    /// <summary>Techo por pasada: un backlog gigante no debe monopolizar el ciclo.</summary>
    private const int MaxPerCycle = 200;

    public async Task<IReadOnlyList<Guid>> FindTenantsWithDueProcessesAsync(
        DateTimeOffset nowUtc, CancellationToken ct = default)
        // IgnoreQueryFilters: aqui todavia no hay tenant fijado. Es un barrido de PLATAFORMA y por eso
        // devuelve solo ids de tenant, ningun dato de negocio; lo que venga despues va acotado.
        //
        // Incluye las que estan SIN PROGRAMAR (NextRunAt == null): son las que quedarian MUERTAS para
        // siempre si nadie les calculo la proxima ventana (por ejemplo, las creadas antes de que este
        // motor existiera - que son TODAS las de hoy). Al visitarlas, RunDueForTenantAsync las repara.
        => await db.ImportProcesses.IgnoreQueryFilters()
            .Where(p => p.IsActive
                && p.ScheduleKind != ImportScheduleKind.Manual
                && (p.NextRunAt == null || p.NextRunAt <= nowUtc))
            .Select(p => p.TenantId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<int> RunDueForTenantAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not Guid tenantId) { return 0; }

        var tz = ScheduledJobRecurrence.ResolveTimeZone(await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.TimeZoneId)
            .FirstOrDefaultAsync(ct));

        var candidates = await db.ImportProcesses
            .Where(p => p.IsActive
                && p.ScheduleKind != ImportScheduleKind.Manual
                && (p.NextRunAt == null || p.NextRunAt <= nowUtc))
            .OrderBy(p => p.NextRunAt)
            .Take(MaxPerCycle)
            .ToListAsync(ct);

        var fired = 0;
        foreach (var process in candidates)
        {
            if (ct.IsCancellationRequested) { break; }

            // AUTO-REPARACION: sin proxima ventana no habia nada que disparar todavia. Se le calcula
            // y se deja lista para el siguiente ciclo, en vez de disparar "ahora" una programacion
            // que nunca se planifico.
            if (process.NextRunAt is null)
            {
                Reschedule(process, nowUtc, tz);
                continue;
            }

            var window = process.NextRunAt.Value;
            try
            {
                var result = await runner.RunNowAsync(process.Id, ImportRunTrigger.Scheduled, window, ct);
                if (result.Ok) { fired++; }
                else
                {
                    // No se lanza excepcion: el runner YA lo dejo en la bitacora (o lo descarto por
                    // idempotencia). Aqui solo interesa que la programacion siga su curso.
                    log.LogInformation("[HORARIO] proceso {Process} ventana {Window:o}: {Msg}",
                        process.Id, window, result.Message);
                }
            }
            catch (Exception ex)
            {
                // El fallo de una programacion no debe frenar a las demas del tenant.
                log.LogError(ex, "[HORARIO] fallo el disparo del proceso {Process}", process.Id);
            }

            // Se reprograma SIEMPRE, haya ido bien o mal. Si no, una programacion que falla una vez
            // se quedaria clavada en la misma ventana y reintentaria en bucle cada minuto.
            Reschedule(process, nowUtc, tz);
        }

        await db.SaveChangesAsync(ct);
        return fired;
    }

    private void Reschedule(Domain.Entities.ImportProcess process, DateTimeOffset nowUtc, TimeZoneInfo tz)
    {
        var next = ImportRecurrence.ComputeNextRun(process, nowUtc, tz);
        process.NextRunAt = next.NextRunAt;

        if (next.Problem == ImportScheduleProblem.Invalid)
        {
            // Se apaga con el motivo A LA VISTA. La alternativa -dejarla activa sin proxima ventana-
            // es el fallo silencioso que este modulo existe para evitar: el operador la veria
            // "activa" y no dispararia nunca.
            process.IsActive = false;
            process.DisabledReason = next.Reason;
            log.LogWarning("[HORARIO] proceso {Process} desactivado: {Reason}", process.Id, next.Reason);
        }
        else if (next.Problem == ImportScheduleProblem.None)
        {
            process.DisabledReason = null;
        }
    }
}
