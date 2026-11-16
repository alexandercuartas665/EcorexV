using System.Collections.Concurrent;
using Ecorex.Application.Scraping;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>Resultado de un tramo de la secuencia.</summary>
public sealed record FlowSequenceTramoResult(
    int Numero, int Desde, int Largo, bool Ok, int Intentos, string? Error, DateTimeOffset FinishedAt);

/// <summary>Estado (en vivo o el ultimo) de la secuencia de tramos de un flujo, para la UI.</summary>
public sealed record FlowSequenceStatus(
    Guid FlowId,
    Guid? ScheduleId,
    string Name,
    ImportRunTrigger Trigger,
    DateTimeOffset StartedAt,
    int Total,
    int Current,
    string Estado,
    DateTimeOffset? NextActionAt,
    IReadOnlyList<FlowSequenceTramoResult> Done,
    bool Finished,
    string? Summary);

/// <summary>
/// Corre la SECUENCIA DE TRAMOS de una programacion de flujo (ADR-0171): el mismo flujo N veces seguidas,
/// cada tramo con su desplazamiento (overrides de corrida, nunca persistidos), esperando a que termine cada
/// corrida, con pausa entre tramos y reintento de los que fallan. La dispara el horario (dispatcher) o el
/// boton "Ejecutar secuencia ahora". Una secuencia por flujo a la vez (candado). El estado vive en memoria:
/// si el proceso se reinicia a mitad, la secuencia se pierde (los tramos ya corridos quedan en la bitacora) y
/// el siguiente horario empieza de nuevo. Singleton.
/// </summary>
public interface IFlowSequenceRunner
{
    /// <summary>true si el flujo tiene una secuencia en curso (corriendo un tramo o en pausa entre tramos).</summary>
    bool IsBusy(Guid flowId);

    /// <summary>Estado de la secuencia en curso del flujo o, si no hay, el de la ultima que termino.</summary>
    FlowSequenceStatus? GetStatus(Guid flowId);

    /// <summary>Arranca la secuencia en segundo plano. false (con motivo) si el flujo ya esta ocupado o la
    /// secuencia no es valida.</summary>
    bool TryStart(Guid tenantId, Guid flowId, Guid? scheduleId, string name, FlowSequenceSpec spec,
        ImportRunTrigger trigger, out string? error);

    /// <summary>Pide detener la secuencia del flujo: no arranca mas tramos (el que esta corriendo termina).</summary>
    bool Cancel(Guid flowId);
}

public sealed class FlowSequenceRunner(
    IBrowserRunService browserRuns,
    IServiceScopeFactory scopeFactory,
    ILogger<FlowSequenceRunner> log,
    TimeProvider? clock = null) : IFlowSequenceRunner
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Cuanto se espera, como mucho, a que termine una corrida del mismo flujo que ya estaba viva
    /// (p.ej. alguien pulso "Importar del bot") antes de dar el tramo por fallido.</summary>
    private static readonly TimeSpan MaxEsperaFlujoOcupado = TimeSpan.FromHours(2);

    private sealed class Live
    {
        public required Guid FlowId;
        public required Guid? ScheduleId;
        public required string Name;
        public required ImportRunTrigger Trigger;
        public required DateTimeOffset StartedAt;
        public required int Total;
        public required CancellationTokenSource Cts;
        public int Current;
        public string Estado = "iniciando";
        public DateTimeOffset? NextActionAt;
        public readonly List<FlowSequenceTramoResult> Done = new();
        public bool Finished;
        public string? Summary;
        public readonly object Gate = new();

        public FlowSequenceStatus Snapshot()
        {
            lock (Gate)
            {
                return new FlowSequenceStatus(FlowId, ScheduleId, Name, Trigger, StartedAt, Total, Current, Estado,
                    NextActionAt, Done.ToList(), Finished, Summary);
            }
        }
    }

    private readonly ConcurrentDictionary<Guid, Live> _live = new();
    private readonly ConcurrentDictionary<Guid, FlowSequenceStatus> _last = new();

    public bool IsBusy(Guid flowId) => _live.ContainsKey(flowId);

    public FlowSequenceStatus? GetStatus(Guid flowId)
        => _live.TryGetValue(flowId, out var l) ? l.Snapshot() : _last.TryGetValue(flowId, out var s) ? s : null;

    public bool Cancel(Guid flowId)
    {
        if (!_live.TryGetValue(flowId, out var l)) { return false; }
        try { l.Cts.Cancel(); } catch (ObjectDisposedException) { }
        lock (l.Gate) { l.Estado = "deteniendo (el tramo en curso termina)"; }
        return true;
    }

    public bool TryStart(Guid tenantId, Guid flowId, Guid? scheduleId, string name, FlowSequenceSpec spec,
        ImportRunTrigger trigger, out string? error)
    {
        error = spec.Validate();
        if (error is not null) { return false; }
        var tramos = spec.Tramos();
        var live = new Live
        {
            FlowId = flowId, ScheduleId = scheduleId, Name = name, Trigger = trigger,
            StartedAt = _clock.GetUtcNow(), Total = tramos.Count, Cts = new CancellationTokenSource(),
        };
        if (!_live.TryAdd(flowId, live))
        {
            error = "El flujo ya tiene una secuencia en curso.";
            return false;
        }
        _ = Task.Run(async () =>
        {
            try { await RunAsync(tenantId, live, spec, tramos); }
            catch (Exception ex)
            {
                log.LogError(ex, "[SECUENCIA] fallo inesperado flow={Flow}", flowId);
                lock (live.Gate) { live.Summary = "Fallo inesperado: " + ex.Message; }
            }
            finally
            {
                lock (live.Gate) { live.Finished = true; live.Estado = "terminada"; live.NextActionAt = null; }
                _last[flowId] = live.Snapshot();
                _live.TryRemove(flowId, out _);
                live.Cts.Dispose();
            }
        }, CancellationToken.None);
        log.LogInformation("[SECUENCIA] iniciada flow={Flow} '{Name}' tramos={N} trigger={Trigger}",
            flowId, name, tramos.Count, trigger);
        return true;
    }

    private void SetEstado(Live l, int current, string estado, DateTimeOffset? next = null)
    {
        lock (l.Gate) { l.Current = current; l.Estado = estado; l.NextActionAt = next; }
    }

    private async Task<bool> DelayAsync(TimeSpan span, CancellationToken ct)
    {
        if (span <= TimeSpan.Zero) { return !ct.IsCancellationRequested; }
        try { await Task.Delay(span, _clock, ct); return true; }
        catch (OperationCanceledException) { return false; }
    }

    private async Task RunAsync(Guid tenantId, Live l, FlowSequenceSpec spec, IReadOnlyList<FlowTramo> tramos)
    {
        var ct = l.Cts.Token;
        string? aborto = null;
        foreach (var t in tramos)
        {
            if (ct.IsCancellationRequested) { aborto = "detenida por el usuario"; break; }
            if (spec.MaxHoras > 0 && _clock.GetUtcNow() - l.StartedAt > TimeSpan.FromHours(spec.MaxHoras))
            {
                aborto = $"supero el tiempo maximo de {spec.MaxHoras} h";
                break;
            }

            // Candado: si otra corrida del mismo flujo esta viva (p.ej. "Importar del bot"), se espera a que
            // termine en vez de montarse encima.
            var esperaHasta = _clock.GetUtcNow() + MaxEsperaFlujoOcupado;
            while (browserRuns.IsFlowRunning(l.FlowId) && _clock.GetUtcNow() < esperaHasta && !ct.IsCancellationRequested)
            {
                SetEstado(l, t.Numero, "esperando a que termine otra corrida del flujo", _clock.GetUtcNow().AddSeconds(30));
                if (!await DelayAsync(TimeSpan.FromSeconds(30), ct)) { break; }
            }
            if (ct.IsCancellationRequested) { aborto = "detenida por el usuario"; break; }

            var intentos = 0;
            bool ok = false, offline = false;
            string? error = null;
            while (true)
            {
                intentos++;
                SetEstado(l, t.Numero, $"corriendo tramo {t.Numero}/{tramos.Count} (desde {t.Desde}, {t.Largo})"
                    + (intentos > 1 ? $" - reintento {intentos - 1}" : ""));
                BrowserRunResult r;
                try
                {
                    var ov = spec.OverridesFor(t);
                    await ContributeAsync(tenantId, l.FlowId, ov, ct);
                    // Sin token de cancelacion: un tramo que arranco termina (cortarlo a mitad deja el navegador
                    // del agente y la bitacora a medias). "Detener" actua entre tramos.
                    r = await browserRuns.RunFlowStepwiseAsync(l.FlowId, tenantId, 0, 1, null,
                        CancellationToken.None, ov, l.Trigger);
                }
                catch (Exception ex) { r = new BrowserRunResult(false, null, null, false, ex.Message); }

                if (r.Offline) { offline = true; error = r.Error ?? "el agente no esta en linea"; break; }
                if (r.Dispatched) { ok = true; error = null; break; }
                error = r.Error ?? "no termino bien";
                if (intentos > spec.Reintentos || ct.IsCancellationRequested) { break; }
                var espera = TimeSpan.FromMinutes(spec.ReintentoMin);
                SetEstado(l, t.Numero, $"tramo {t.Numero} fallo; reintento en {spec.ReintentoMin} min", _clock.GetUtcNow() + espera);
                if (!await DelayAsync(espera, ct)) { break; }
            }

            lock (l.Gate) { l.Done.Add(new FlowSequenceTramoResult(t.Numero, t.Desde, t.Largo, ok, intentos, error, _clock.GetUtcNow())); }
            log.LogInformation("[SECUENCIA] flow={Flow} tramo {N}/{Total} desde={Desde} largo={Largo} ok={Ok} intentos={Int} err={Err}",
                l.FlowId, t.Numero, tramos.Count, t.Desde, t.Largo, ok, intentos, error);

            // Sin agente no tiene sentido seguir: el resto fallaria igual. El horario lo vuelve a intentar.
            if (offline) { aborto = "el agente quedo fuera de linea"; break; }

            if (t.Numero < tramos.Count && spec.PausaMin > 0)
            {
                var pausa = TimeSpan.FromMinutes(spec.PausaMin);
                SetEstado(l, t.Numero, $"pausa de {spec.PausaMin} min antes del tramo {t.Numero + 1}", _clock.GetUtcNow() + pausa);
                if (!await DelayAsync(pausa, ct)) { aborto = "detenida por el usuario"; break; }
            }
        }

        await WriteSummaryAsync(tenantId, l, spec, tramos.Count, aborto);
    }

    private async Task ContributeAsync(Guid tenantId, Guid flowId, Dictionary<string, string> ov, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        using (AmbientTenantContext.Begin(tenantId))
        {
            foreach (var p in scope.ServiceProvider.GetServices<IFlowRunOverrideProvider>())
            {
                await p.ContributeAsync(flowId, ov, ct);
            }
        }
    }

    /// <summary>Deja UNA fila de resumen en la bitacora del flujo (ademas de la de cada tramo).</summary>
    private async Task WriteSummaryAsync(Guid tenantId, Live l, FlowSequenceSpec spec, int total, string? aborto)
    {
        List<FlowSequenceTramoResult> done;
        lock (l.Gate) { done = l.Done.ToList(); }
        var okN = done.Count(d => d.Ok);
        var fallos = done.Where(d => !d.Ok).ToList();
        static string R(FlowSequenceTramoResult d) => d.Largo == 1 ? $"{d.Desde}" : $"{d.Desde}-{d.Desde + d.Largo - 1}";
        var resumen = $"Secuencia '{l.Name}': {okN}/{total} tramos ok"
            + (done.Count < total ? $", {total - done.Count} sin correr" : "")
            + (fallos.Count > 0 ? $"; fallaron (dias {string.Join(", ", fallos.Select(f => R(f) + ": " + Corto(f.Error)))})" : "")
            + (aborto is not null ? $"; {aborto}" : "")
            + $". {spec.Describe()}.";
        lock (l.Gate) { l.Summary = resumen; }
        try
        {
            using var scope = scopeFactory.CreateScope();
            using (AmbientTenantContext.Begin(tenantId))
            {
                var runLog = scope.ServiceProvider.GetRequiredService<IScrapeFlowRunLog>();
                var corr = "seq" + Guid.NewGuid().ToString("N")[..8];
                await runLog.OpenAsync(l.FlowId, l.Trigger, l.StartedAt, corr, total);
                await runLog.CloseAsync(corr, fallos.Count == 0 && aborto is null && done.Count == total, 0, 0, 0, resumen);
            }
        }
        catch (Exception ex) { log.LogWarning(ex, "[SECUENCIA] no se pudo escribir el resumen flow={Flow}", l.FlowId); }
    }

    private static string Corto(string? s)
    {
        s = (s ?? "error").Trim();
        return s.Length <= 80 ? s : s[..77] + "...";
    }
}
