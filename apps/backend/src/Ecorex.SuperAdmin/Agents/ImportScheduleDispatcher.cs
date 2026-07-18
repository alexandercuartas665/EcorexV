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
    /// <summary>Barrido de PLATAFORMA: que tenants tienen algo que hacer (vencido O esperando a su
    /// agente). Devuelve solo ids.</summary>
    Task<IReadOnlyList<Guid>> FindTenantsWithWorkAsync(DateTimeOffset nowUtc, CancellationToken ct = default);

    /// <summary>Dispara lo vencido y reintenta lo que esperaba a su agente, del tenant fijado en el
    /// contexto. Devuelve cuantas disparo.</summary>
    Task<int> RunDueForTenantAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
}

public sealed class ImportScheduleDispatcher(
    IApplicationDbContext db,
    ITenantContext tenantContext,
    IProcessRunner runner,
    IBrowserRunService browserRuns,
    IAgentRegistry registry,
    ILogger<ImportScheduleDispatcher> log) : IImportScheduleDispatcher
{
    /// <summary>Techo por pasada: un backlog gigante no debe monopolizar el ciclo.</summary>
    private const int MaxPerCycle = 200;

    public async Task<IReadOnlyList<Guid>> FindTenantsWithWorkAsync(
        DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        // IgnoreQueryFilters: aqui todavia no hay tenant fijado. Es un barrido de PLATAFORMA y por eso
        // devuelve solo ids de tenant, ningun dato de negocio; lo que venga despues va acotado.
        //
        // Vencidas: incluye las SIN PROGRAMAR (NextRunAt == null), que quedarian MUERTAS para siempre si
        // nadie les calculo la proxima ventana; al visitarlas, RunDueForTenantAsync las repara.
        var due = db.ImportProcesses.IgnoreQueryFilters()
            .Where(p => p.IsActive
                && p.ScheduleKind != ImportScheduleKind.Manual
                && (p.NextRunAt == null || p.NextRunAt <= nowUtc))
            .Select(p => p.TenantId);

        // Esperando a su agente: las que fallaron por agente offline y aun no se han alcanzado. Sin
        // esto, un tenant con SOLO cargas pendientes (nada vencido ahora) nunca se reintentaria.
        var pending = db.ImportProcesses.IgnoreQueryFilters()
            .Where(p => p.PendingSince != null)
            .Select(p => p.TenantId);

        return await due.Concat(pending).Distinct().ToListAsync(ct);
    }

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
        var processedIds = new HashSet<Guid>();
        foreach (var process in candidates)
        {
            if (ct.IsCancellationRequested) { break; }
            processedIds.Add(process.Id);

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
                // Un proceso puede programar un FLUJO de extraccion (Ola 5) en vez de refrescar un
                // contenedor. Se ramifica: el flujo lo ejecuta el runtime del Navegador y su corrida vive
                // en ScrapeFlowRun (no ImportRun). El manejo offline se reusa parqueando PendingSince.
                if (process.FlowId is Guid flowId)
                {
                    if (await FireFlowAsync(process, flowId, window, ct)) { fired++; }
                }
                else
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

        // Segundo pase: cargas que se quedaron esperando a su agente. Va DESPUES del pase de vencidas
        // (que ya limpia PendingSince de las que ademas tocaban ahora) y solo intenta las que su agente
        // ya volvio: reintentar con el agente aun caido solo generaria otra corrida PendingOffline cada
        // minuto (spam). Por eso el gate por IsOnline es lo que hace que esto se dispare AL RECONECTAR.
        fired += await RetryPendingAsync(processedIds, nowUtc, ct);
        return fired;
    }

    private async Task<int> RetryPendingAsync(HashSet<Guid> alreadyRun, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var pending = await db.ImportProcesses
            .Where(p => p.IsActive && p.ScheduleKind != ImportScheduleKind.Manual && p.PendingSince != null)
            .Take(MaxPerCycle)
            .ToListAsync(ct);

        var retried = 0;
        var flowChanged = false;
        foreach (var process in pending)
        {
            if (ct.IsCancellationRequested) { break; }
            if (alreadyRun.Contains(process.Id)) { continue; } // el pase de vencidas ya lo toco.

            // Proceso de FLUJO (Ola 5): el cliente esta en el flujo, no en el proceso. Mismo gate por
            // IsOnline (solo se reintenta cuando el agente reconecta, para no generar otra corrida
            // PendingOffline cada minuto).
            if (process.FlowId is Guid flowId)
            {
                var clientRow = await db.ScrapeFlows.AsNoTracking()
                    .Where(f => f.Id == flowId).Select(f => f.ClientId).FirstOrDefaultAsync(ct);
                if (clientRow is not Guid cid) { continue; }
                var pub = await db.DataClients.AsNoTracking()
                    .Where(c => c.Id == cid).Select(c => c.ClientId).FirstOrDefaultAsync(ct);
                if (pub is null || !registry.IsOnline(pub)) { continue; } // aun no ha vuelto.
                try
                {
                    var res = await FireFlowAsync(process, flowId, nowUtc, ct);
                    if (res) { retried++; flowChanged = true; log.LogInformation("[HORARIO] flujo {Flow}: agente reconecto, carga pendiente reintentada", flowId); }
                }
                catch (Exception ex) { log.LogError(ex, "[HORARIO] fallo el reintento del flujo {Flow}", flowId); }
                continue;
            }

            if (process.ClientId is not Guid clientRowId) { continue; }

            var publicId = await db.DataClients.AsNoTracking()
                .Where(c => c.Id == clientRowId).Select(c => c.ClientId).FirstOrDefaultAsync(ct);
            if (publicId is null || !registry.IsOnline(publicId)) { continue; } // aun no ha vuelto.

            try
            {
                // Ventana = ahora: es una corrida de PONERSE AL DIA, no la ventana perdida (que ya
                // consta en la bitacora como PendingOffline). RunNow limpia PendingSince al despachar.
                var result = await runner.RunNowAsync(process.Id, ImportRunTrigger.Scheduled, nowUtc, ct);
                if (result.Ok)
                {
                    retried++;
                    log.LogInformation("[HORARIO] proceso {Process}: agente reconecto, carga pendiente reintentada", process.Id);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "[HORARIO] fallo el reintento del proceso {Process}", process.Id);
            }
        }
        // El path de import guarda via ProcessRunner (su propio SaveChanges); el de flujo cambia el
        // proceso aqui (PendingSince), asi que se persiste si hubo alguno.
        if (flowChanged) { await db.SaveChangesAsync(ct); }
        return retried;
    }

    /// <summary>Dispara un proceso que apunta a un flujo: el runtime del Navegador lo ejecuta y su
    /// corrida va a ScrapeFlowRun. Si el agente esta offline, se parquea PendingSince para reintentar al
    /// reconectar (igual que el import). Devuelve true si se despacho.</summary>
    private async Task<bool> FireFlowAsync(Domain.Entities.ImportProcess process, Guid flowId, DateTimeOffset window, CancellationToken ct)
    {
        if (tenantContext.TenantId is not Guid tenantId) { return false; }
        var result = await browserRuns.RunFlowNowAsync(flowId, tenantId, ImportRunTrigger.Scheduled, ct);
        process.LastRunAt = window;
        if (result.Offline)
        {
            process.PendingSince ??= window; // parquea la mas vieja; se limpia al alcanzarlo.
            log.LogInformation("[HORARIO] flujo {Flow} offline; se reintentara cuando el agente vuelva", flowId);
            return false;
        }
        process.PendingSince = null;
        return result.Dispatched;
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
