using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Contactos;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Agents;
using Ecorex.SuperAdmin.Auth;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Auto-run de las BUSQUEDAS DE CONTACTOS programadas (Bolsa 000740). Cada tick barre CROSS-TENANT las
/// <see cref="Ecorex.Domain.Entities.ContactSearchDefinition"/> ACTIVAS con horarios (SchedulesJson),
/// determina cuales estan VENCIDAS (segun el slot + LastRunAt, sin re-correr el mismo periodo) y ejecuta
/// cada una en el contexto de SU tenant (<see cref="AmbientTenantContext.Begin"/> + IContactSearchRunner),
/// reusando la misma ruta que el boton "Correr ahora".
///
/// - Respeta el tope 20/dia social (lo aplica el propio runner) y no falla el tick si la Colmena esta
///   apagada: el runner devuelve error, se registra y se reintenta en el siguiente tick.
/// - Vive en Ecorex.SuperAdmin (como los demas workers): el compose de prod solo levanta ecorex-app.
/// - Se apaga con la env ECOREX_DISABLE_WORKERS=true (junto a los demas motores) o con el flag de config
///   ContactSearchScheduler:Enabled=false. Intervalo por config ContactSearchScheduler:TickSeconds.
/// - Hora: el slot RunTime "HH:mm" se interpreta en UTC (el auto-run no requiere precision de zona; el
///   usuario fija RunTime a la hora UTC deseada). LastRunAt lo actualiza el runner al correr.
/// </summary>
public sealed class ContactSearchScheduleWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IConfiguration config,
    ILogger<ContactSearchScheduleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("ContactSearchScheduler:Enabled", true))
        {
            logger.LogInformation("Auto-run de busquedas programadas DESHABILITADO (ContactSearchScheduler:Enabled=false).");
            return;
        }
        var seconds = Math.Clamp(config.GetValue("ContactSearchScheduler:TickSeconds", 120), 30, 3600);
        var period = TimeSpan.FromSeconds(seconds);
        logger.LogInformation("Auto-run de busquedas de contactos programadas iniciado; barrido cada {Period}.", period);

        using var timer = new PeriodicTimer(period);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunCycleAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Un ciclo fallido NUNCA mata al worker: se registra y se reintenta.
                logger.LogError(ex, "Fallo el ciclo de busquedas programadas; se reintenta en {Period}.", period);
            }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) { break; } }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        var nowUtc = timeProvider.GetUtcNow();

        // 1) Candidatas CROSS-TENANT (solo lo minimo): activas con horarios.
        List<ScheduledCandidate> candidates;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            candidates = await db.ContactSearchDefinitions.IgnoreQueryFilters().AsNoTracking()
                .Where(d => d.IsActive && d.SchedulesJson != null)
                .Select(d => new ScheduledCandidate(d.Id, d.TenantId, d.SchedulesJson!, d.LastRunAt))
                .ToListAsync(ct);
        }

        // 2) Filtrar las VENCIDAS.
        var due = candidates.Where(c => IsDue(ParseSlots(c.SchedulesJson), c.LastRunAt, nowUtc)).ToList();
        if (due.Count == 0) { return; }

        // 3) Correr cada una EN SU TENANT (scope propio + tenant ambiente); el runner actualiza LastRunAt.
        foreach (var c in due)
        {
            if (ct.IsCancellationRequested) { break; }
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                using (AmbientTenantContext.Begin(c.TenantId))
                {
                    var runner = scope.ServiceProvider.GetRequiredService<IContactSearchRunner>();
                    var res = await runner.RunAsync(c.Id, ct);
                    if (res.Ok)
                    {
                        logger.LogInformation("Busqueda programada {Id} (tenant {Tenant}): corrio, {Created} contacto(s).",
                            c.Id, c.TenantId, res.Created);
                    }
                    else
                    {
                        // Colmena apagada / tope diario / etc.: NO es fatal; se reintenta en el proximo tick.
                        logger.LogInformation("Busqueda programada {Id} (tenant {Tenant}): no corrio esta vez ({Error}).",
                            c.Id, c.TenantId, res.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                // El fallo de una busqueda no frena a las demas.
                logger.LogError(ex, "Fallo la busqueda programada {Id} (tenant {Tenant}).", c.Id, c.TenantId);
            }
        }
    }

    private static IReadOnlyList<ContactSearchScheduleSlot> ParseSlots(string json)
    {
        try { return JsonSerializer.Deserialize<List<ContactSearchScheduleSlot>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>Vencida si ALGUN slot tiene una ocurrencia programada (&lt;= ahora) posterior a LastRunAt.</summary>
    private static bool IsDue(IReadOnlyList<ContactSearchScheduleSlot> slots, DateTimeOffset? lastRunAt, DateTimeOffset nowUtc)
    {
        foreach (var slot in slots)
        {
            if (LastScheduledOccurrence(slot, nowUtc) is DateTimeOffset occ && (lastRunAt is null || lastRunAt.Value < occ))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Ultima ocurrencia programada del slot &lt;= now (UTC). Null si Manual.</summary>
    private static DateTimeOffset? LastScheduledOccurrence(ContactSearchScheduleSlot slot, DateTimeOffset now)
    {
        if (slot.Frequency == ContactSearchSchedule.Manual) { return null; }
        var rt = ParseTime(slot.RunTime);
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, rt.Hours, rt.Minutes, 0, TimeSpan.Zero);
        switch (slot.Frequency)
        {
            case ContactSearchSchedule.Diaria:
                return today <= now ? today : today.AddDays(-1);
            case ContactSearchSchedule.Semanal:
                var targetDow = Math.Clamp(slot.DayOfWeek ?? (int)now.DayOfWeek, 0, 6);
                var back = (((int)now.DayOfWeek - targetDow) % 7 + 7) % 7;
                var cand = today.AddDays(-back);
                return cand <= now ? cand : cand.AddDays(-7);
            case ContactSearchSchedule.Mensual:
                // Se acota a 28 para no saltar meses cortos (v1: precision de dia, no de fin-de-mes exacto).
                var dom = Math.Clamp(slot.DayOfMonth ?? 1, 1, 28);
                var m = new DateTimeOffset(now.Year, now.Month, dom, rt.Hours, rt.Minutes, 0, TimeSpan.Zero);
                if (m > now) { var p = now.AddMonths(-1); m = new DateTimeOffset(p.Year, p.Month, dom, rt.Hours, rt.Minutes, 0, TimeSpan.Zero); }
                return m;
            default:
                return null;
        }
    }

    private static (int Hours, int Minutes) ParseTime(string? runTime)
    {
        if (!string.IsNullOrWhiteSpace(runTime) && TimeSpan.TryParse(runTime, out var ts) && ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1))
        {
            return (ts.Hours, ts.Minutes);
        }
        return (8, 0); // por defecto 08:00 UTC
    }

    private readonly record struct ScheduledCandidate(Guid Id, Guid TenantId, string SchedulesJson, DateTimeOffset? LastRunAt);
}
