using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Bitacora de corridas de un flujo de extraccion (Ola 3). Espejo de <c>IImportRunLog</c> pero sobre
/// <see cref="ScrapeFlowRun"/>: el ciclo tiene dos mitades separadas en el tiempo (se abre al
/// despachar, se cierra cuando el Navegador responde por el hub) y el puente es el correlationId.
/// Al cerrar, ademas, sella el resumen visible en el propio flujo (LastRunAt/LastResultSummary/Status).
/// </summary>
public interface IScrapeFlowRunLog
{
    /// <summary>Abre la corrida en Running y devuelve su id.</summary>
    Task<Guid> OpenAsync(Guid flowId, ImportRunTrigger trigger, DateTimeOffset firedAt,
        string correlationId, int stepCount, CancellationToken ct = default);

    /// <summary>Cierra por correlationId con el resultado del Navegador. Idempotente: si ya estaba
    /// cerrada (ej. vencio el plazo y el agente respondio tarde), no la toca.</summary>
    Task CloseAsync(string correlationId, bool ok, int inserted, int updated, int deleted,
        string? detail, CancellationToken ct = default);

    /// <summary>Cierra una corrida que no llego a despacharse (compilacion fallida, sin agente...).</summary>
    Task FailAsync(Guid runId, string detail, CancellationToken ct = default);

    /// <summary>Marca la corrida como PendingOffline: el agente no estaba. No es un fallo; en Ola 5 el
    /// horario la reintenta al reconectar (como ya hace el Contenedor de datos).</summary>
    Task MarkOfflineAsync(Guid runId, string detail, CancellationToken ct = default);
}

public sealed class ScrapeFlowRunLog(IApplicationDbContext db, ILogger<ScrapeFlowRunLog> log) : IScrapeFlowRunLog
{
    public async Task<Guid> OpenAsync(Guid flowId, ImportRunTrigger trigger, DateTimeOffset firedAt,
        string correlationId, int stepCount, CancellationToken ct = default)
    {
        var run = new ScrapeFlowRun
        {
            FlowId = flowId,
            FiredAt = firedAt,
            Trigger = trigger,
            Result = ImportRunResult.Running,
            CorrelationId = correlationId,
            StepCount = stepCount
        };
        db.ScrapeFlowRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run.Id;
    }

    public async Task CloseAsync(string correlationId, bool ok, int inserted, int updated, int deleted,
        string? detail, CancellationToken ct = default)
    {
        var run = await db.ScrapeFlowRuns.FirstOrDefaultAsync(r => r.CorrelationId == correlationId, ct);
        if (run is null || run.Result != ImportRunResult.Running) { return; }

        run.Result = ok ? ImportRunResult.Ok : ImportRunResult.Error;
        run.Inserted = inserted;
        run.Updated = updated;
        run.Deleted = deleted;
        run.Detail = Trim(detail);
        run.FinishedAt = DateTimeOffset.UtcNow;
        await SealFlowAsync(run, ok, detail, ct);
        await db.SaveChangesAsync(ct);
        log.LogInformation("[NAV-RUN] corr={Corr} cerrada ok={Ok} ins={Ins} upd={Upd} del={Del}",
            correlationId, ok, inserted, updated, deleted);
    }

    public async Task FailAsync(Guid runId, string detail, CancellationToken ct = default)
    {
        var run = await db.ScrapeFlowRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Result != ImportRunResult.Running) { return; }
        run.Result = ImportRunResult.Error;
        run.Detail = Trim(detail);
        run.FinishedAt = DateTimeOffset.UtcNow;
        await SealFlowAsync(run, false, detail, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkOfflineAsync(Guid runId, string detail, CancellationToken ct = default)
    {
        var run = await db.ScrapeFlowRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Result != ImportRunResult.Running) { return; }
        run.Result = ImportRunResult.PendingOffline;
        run.Detail = Trim(detail);
        run.FinishedAt = DateTimeOffset.UtcNow;
        // No se toca el Status del flujo: un agente dormido no deja el flujo "con errores".
        run.Flow ??= await db.ScrapeFlows.FirstOrDefaultAsync(f => f.Id == run.FlowId, ct);
        if (run.Flow is not null) { run.Flow.LastRunAt = run.FiredAt; run.Flow.LastResultSummary = Trim(detail); }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Refleja el resultado en el propio flujo (lo que ve el operador en el hero/lista sin
    /// abrir la bitacora). Un fallo deja el flujo "con errores"; un ok lo devuelve a activo.</summary>
    private async Task SealFlowAsync(ScrapeFlowRun run, bool ok, string? detail, CancellationToken ct)
    {
        var flow = await db.ScrapeFlows.FirstOrDefaultAsync(f => f.Id == run.FlowId, ct);
        if (flow is null) { return; }
        flow.LastRunAt = run.FiredAt;
        flow.LastResultSummary = Trim(detail) ?? (ok ? $"{run.Inserted} filas" : "Error");
        flow.Status = ok
            ? (flow.Status == ScrapeSourceStatus.Error ? ScrapeSourceStatus.Active : flow.Status)
            : ScrapeSourceStatus.Error;
    }

    private static string? Trim(string? s) =>
        s is null ? null : s.Length <= 600 ? s : s[..597] + "...";
}
