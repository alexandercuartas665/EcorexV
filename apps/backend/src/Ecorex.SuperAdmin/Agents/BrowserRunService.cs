using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Contracts.Agent;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Auth;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>Lo que devuelve "Ejecutar ahora": si se despacho, a que corrida corresponde, y si quedo
/// esperando al agente o fallo antes de salir.</summary>
public sealed record BrowserRunResult(bool Dispatched, Guid? RunId, string? CorrelationId, bool Offline, string? Error);

/// <summary>
/// Runtime de los flujos de extraccion (modulo 000730, Olas 3-4). Ejecuta el flujo PASO A PASO en el
/// sub-agente Navegador: agrupa los pasos deterministas consecutivos en un tramo, los empuja por el
/// canal request/response (<see cref="IBrowserActionChannel"/>) y AWAITA su resultado antes de seguir;
/// cada paso de IA lo resuelve el <see cref="IAiStepOrchestrator"/> (bucle agente<->navegador). Las
/// filas de los pasos Extract / del paso de IA se ingieren con <see cref="IRowIngestService"/>, y la
/// corrida queda en la bitacora dedicada (<see cref="IScrapeFlowRunLog"/>, ADR-0042).
///
/// La ejecucion corre en SEGUNDO PLANO: "Ejecutar ahora" valida, abre la corrida, comprueba que el
/// agente este en linea y lanza la ejecucion sin bloquear la UI (que llega despues por el hub). Si el
/// agente no esta, la corrida queda PendingOffline (la Ola 5 la reintenta al reconectar).
/// Singleton (no guarda estado entre corridas; cada una corre en su propio scope).
/// </summary>
public interface IBrowserRunService
{
    Task<BrowserRunResult> RunFlowNowAsync(Guid flowId, Guid tenantId, ImportRunTrigger trigger, CancellationToken ct = default);

    /// <summary>Cierra corridas que quedaron "Running" colgadas (p.ej. el servidor se reinicio a mitad).
    /// Lo llama el worker; los timeouts por accion los maneja el canal, esto es la red de seguridad.</summary>
    Task SweepAsync(CancellationToken ct = default);
}

public sealed class BrowserRunService(
    IAgentRegistry registry,
    IBrowserActionChannel channel,
    IServiceScopeFactory scopeFactory,
    IAgentActivityLog activity,
    ILogger<BrowserRunService> log,
    TimeProvider? clock = null) : IBrowserRunService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Una corrida Running mas vieja que esto se da por colgada (el canal ya habria fallado sus
    /// acciones; esto solo limpia lo que quedo tras un reinicio del proceso).</summary>
    private static readonly TimeSpan StaleRunAge = TimeSpan.FromMinutes(20);

    public async Task<BrowserRunResult> RunFlowNowAsync(Guid flowId, Guid tenantId, ImportRunTrigger trigger,
        CancellationToken ct = default)
    {
        var runCorr = NewCorr();
        var firedAt = _clock.GetUtcNow();

        using var scope = scopeFactory.CreateScope();
        using (AmbientTenantContext.Begin(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var runLog = scope.ServiceProvider.GetRequiredService<IScrapeFlowRunLog>();
            var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();

            var flow = await db.ScrapeFlows.Include(f => f.Steps).Include(f => f.Variables)
                .FirstOrDefaultAsync(f => f.Id == flowId, ct);
            if (flow is null)
            {
                return new BrowserRunResult(false, null, null, false, "El flujo no existe o no es de este tenant.");
            }

            var runId = await runLog.OpenAsync(flowId, trigger, firedAt, runCorr, flow.Steps.Count, ct);

            if (flow.ClientId is not Guid clientPk)
            {
                await runLog.FailAsync(runId, "El flujo no tiene un agente asignado.", ct);
                return new BrowserRunResult(false, runId, runCorr, false, "El flujo no tiene un agente asignado.");
            }
            var client = await db.DataClients.FirstOrDefaultAsync(c => c.Id == clientPk && c.IsActive, ct);
            if (client is null)
            {
                await runLog.FailAsync(runId, "El agente asignado no existe o esta inactivo.", ct);
                return new BrowserRunResult(false, runId, runCorr, false, "El agente asignado no existe o esta inactivo.");
            }
            if (flow.Steps.Count == 0)
            {
                await runLog.FailAsync(runId, "El flujo no tiene pasos que ejecutar.", ct);
                return new BrowserRunResult(false, runId, runCorr, false, "El flujo no tiene pasos que ejecutar.");
            }

            if (!registry.IsOnline(client.ClientId))
            {
                await runLog.MarkOfflineAsync(runId, "El agente asignado no estaba en linea. La corrida queda esperando.", ct);
                return new BrowserRunResult(false, runId, runCorr, true, null);
            }

            string? secret = null;
            if (client.ClientSecretEncrypted is not null)
            {
                try { secret = protector.Unprotect(client.ClientSecretEncrypted); } catch { /* ilegible */ }
            }
            var variables = DecryptVariables(flow.Variables, protector);

            // Ejecucion en SEGUNDO PLANO: no se awaita (la UI recibe "despachado" y el resultado llega a
            // la bitacora al terminar). Corre en su propio scope+tenant, con todo el manejo de fallos
            // dentro para no dejar la corrida "Running" para siempre.
            _ = Task.Run(() => ExecuteFlowAsync(flowId, tenantId, runCorr, client.ClientId, secret, variables), CancellationToken.None);

            log.LogInformation("[NAV-RUN] lanzada corr={Corr} flow={Flow} client={Client}", runCorr, flowId, client.ClientId);
            return new BrowserRunResult(true, runId, runCorr, false, null);
        }
    }

    /// <summary>Ejecuta el flujo paso a paso, en su propio scope. Deterministas por tramos (canal),
    /// pasos de IA por el orquestador. Cierra la corrida al terminar, pase lo que pase.</summary>
    private async Task ExecuteFlowAsync(Guid flowId, Guid tenantId, string runCorr, string clientId,
        string? secret, IReadOnlyDictionary<string, string> variables)
    {
        using var scope = scopeFactory.CreateScope();
        using (AmbientTenantContext.Begin(tenantId))
        {
            var runLog = scope.ServiceProvider.GetRequiredService<IScrapeFlowRunLog>();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var ingest = scope.ServiceProvider.GetRequiredService<IRowIngestService>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IAiStepOrchestrator>();

            int ins = 0, upd = 0, del = 0;
            try
            {
                var flow = await db.ScrapeFlows.Include(f => f.Steps)
                    .FirstOrDefaultAsync(f => f.Id == flowId, CancellationToken.None);
                if (flow is null) { await runLog.CloseAsync(runCorr, false, 0, 0, 0, "El flujo desaparecio."); return; }
                var steps = flow.Steps.OrderBy(s => s.Order).ToList();

                // Paginacion controlada (Ola 5): si el flujo define una variable de pagina + rango, se
                // repite entero por cada pagina, sustituyendo {{PAGINA}}. Sin rango, corre una vez.
                var pages = ResolvePages(flow);
                var vars = new Dictionary<string, string>(variables, StringComparer.Ordinal);
                var notes = new List<string>();

                foreach (var page in pages)
                {
                    if (flow.PageVar is { } pv && !string.IsNullOrWhiteSpace(pv)) { vars[pv] = page.ToString(); }

                    foreach (var segment in Segment(steps))
                    {
                        if (segment.Ai is { } aiStep)
                        {
                            var target = aiStep.TargetContainerId ?? flow.ContainerId
                                ?? throw new ScrapeCompileException($"El paso de IA '{aiStep.Name}' no tiene tabla destino.");
                            var aiStarted = DateTimeOffset.UtcNow;
                            var outcome = await orchestrator.RunAsync(new AiStepContext(
                                clientId, tenantId, aiStep.Instruction ?? "", target,
                                ParseAllowList(aiStep.ToolAllowListJson), aiStep.MaxSteps ?? 0, aiStep.MaxSeconds ?? 0,
                                aiStep.AiProviderId, secret), CancellationToken.None);
                            // Bitacora transversal (ADR-0045): 1 registro resumen por paso de IA (todo su bucle).
                            await activity.RecordAsync(new AgentActivityEntry(
                                tenantId, clientId, null, AgentActivityKind.Browser, NewCorr(),
                                $"Flujo: {flow.Name} (IA: {aiStep.Name})", outcome.Ok, aiStarted, DateTimeOffset.UtcNow,
                                outcome.Ok ? $"{outcome.Inserted} filas, {outcome.RoundsUsed} rondas" : outcome.Error));
                            if (!outcome.Ok) { await runLog.CloseAsync(runCorr, false, ins, upd, del, outcome.Error); return; }
                            ins += outcome.Inserted; upd += outcome.Updated; del += outcome.Deleted;
                        }
                        else
                        {
                            var segCorr = NewCorr();
                            var compiled = ScrapeFlowCompiler.CompileSteps(segment.Steps, flow.ContainerId, vars, segCorr, secret);
                            if (compiled.Actions.Count == 0) { continue; }

                            var timeout = TimeSpan.FromSeconds(60 + compiled.Actions.Sum(a => (a.WaitMs ?? 0) / 1000.0));
                            var req = new BrowserRequestMsg(segCorr, tenantId.ToString(), compiled.Actions);
                            var started = DateTimeOffset.UtcNow;
                            var result = await channel.ExecuteAsync(clientId, req, timeout, CancellationToken.None);
                            // Bitacora transversal de agentes (ADR-0045): 1 registro resumen por tramo despachado.
                            await activity.RecordAsync(new AgentActivityEntry(
                                tenantId, clientId, null, AgentActivityKind.Browser, segCorr, $"Flujo: {flow.Name}",
                                result.Ok, started, DateTimeOffset.UtcNow,
                                result.Ok ? $"{NavUrlOf(compiled)}{compiled.Actions.Count} acciones" : FirstError(result)));
                            if (!result.Ok)
                            {
                                await runLog.CloseAsync(runCorr, false, ins, upd, del, FirstError(result) ?? "El Navegador reporto un error.");
                                return;
                            }

                            // Advertencias (Ola 5): si la etiqueta de un paso aparece en lo que devolvio el
                            // tramo, se detiene (Stop) o se anota (Notify). Deteccion sobre el texto
                            // devuelto (Html/Eval); un endurecimiento mas fino queda como backlog.
                            var haystack = string.Join("\n", result.Results.Select(r => r.Value ?? ""));
                            foreach (var ws in segment.Steps.Where(s => s.WarningAction != ScrapeWarningAction.None && !string.IsNullOrWhiteSpace(s.WarningLabel)))
                            {
                                if (haystack.Contains(ws.WarningLabel!, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (ws.WarningAction == ScrapeWarningAction.Stop)
                                    {
                                        throw new InvalidOperationException($"Advertencia '{ws.WarningLabel}' detectada en '{ws.Name}': corrida detenida.");
                                    }
                                    notes.Add($"advertencia '{ws.WarningLabel}' en '{ws.Name}'");
                                }
                            }

                            foreach (var bind in compiled.Extracts)
                            {
                                var res = result.Results.FirstOrDefault(r => r.Index == bind.ActionIndex);
                                if (res is null || !res.Ok)
                                {
                                    throw new InvalidOperationException(
                                        $"El paso de extraccion #{bind.ActionIndex + 1} no devolvio datos: {res?.Error ?? "sin resultado"}.");
                                }
                                var rows = ScrapeRowIngest.ParseRows(res.Value);
                                var (i, u, d) = await ScrapeRowIngest.IngestAsync(ingest, db, bind.TargetContainerId, tenantId, bind.MappingJson, rows, CancellationToken.None);
                                ins += i; upd += u; del += d;
                            }
                        }
                    }
                }

                var detail = (ins > 0 ? $"{ins} filas extraidas" : "Flujo ejecutado")
                    + (pages.Count > 1 ? $" ({pages.Count} paginas)" : "")
                    + (notes.Count > 0 ? $"; {string.Join("; ", notes)}" : "") + ".";
                await runLog.CloseAsync(runCorr, true, ins, upd, del, detail);
                log.LogInformation("[NAV-RUN] corr={Corr} OK ins={Ins} upd={Upd} del={Del} pages={Pages}", runCorr, ins, upd, del, pages.Count);
            }
            catch (TimeoutException ex)
            {
                await runLog.CloseAsync(runCorr, false, ins, upd, del, ex.Message);
            }
            catch (ScrapeCompileException ex)
            {
                await runLog.CloseAsync(runCorr, false, ins, upd, del, ex.Message);
            }
            catch (Exception ex)
            {
                await runLog.CloseAsync(runCorr, false, ins, upd, del, ex.Message);
                log.LogError(ex, "[NAV-RUN] corr={Corr} fallo la ejecucion", runCorr);
            }
        }
    }

    public async Task SweepAsync(CancellationToken ct = default)
    {
        // Barrido de plataforma (sin tenant fijado): cierra las corridas colgadas en Running. IgnoreQuery
        // Filters porque aqui no hay tenant; se cierran directas (el update no cruza datos entre tenants,
        // solo sella su propia fila).
        var cutoff = _clock.GetUtcNow() - StaleRunAge;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var stale = await db.ScrapeFlowRuns.IgnoreQueryFilters()
            .Where(r => r.Result == ImportRunResult.Running && r.FiredAt < cutoff)
            .ToListAsync(ct);
        if (stale.Count == 0) { return; }
        foreach (var run in stale)
        {
            run.Result = ImportRunResult.Error;
            run.Detail = "La corrida quedo colgada (posible reinicio del servidor) y se cerro.";
            run.FinishedAt = _clock.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);
        log.LogWarning("[NAV-RUN] cerradas {N} corridas colgadas", stale.Count);
    }

    // ---- Segmentacion: tramos deterministas consecutivos + cada paso de IA aparte ----

    /// <summary>Paginas a recorrer. Sin variable/rango valido, una sola pasada ("pagina" 0). Con rango,
    /// [from..to] acotado a un techo por seguridad (un rango enorme no debe colgar una corrida).</summary>
    private const int MaxPages = 500;
    private static IReadOnlyList<int> ResolvePages(ScrapeFlow flow)
    {
        if (string.IsNullOrWhiteSpace(flow.PageVar) || flow.PageFrom is not int from || flow.PageTo is not int to || to < from)
        {
            return new[] { 0 };
        }
        var count = Math.Min(to - from + 1, MaxPages);
        return Enumerable.Range(from, count).ToList();
    }

    private sealed record StepSegment(IReadOnlyList<ScrapeStep> Steps, ScrapeStep? Ai);

    private static IEnumerable<StepSegment> Segment(IReadOnlyList<ScrapeStep> steps)
    {
        var buffer = new List<ScrapeStep>();
        foreach (var s in steps)
        {
            if (s.Kind == ScrapeStepKind.Ai)
            {
                if (buffer.Count > 0) { yield return new StepSegment(buffer.ToList(), null); buffer.Clear(); }
                yield return new StepSegment(Array.Empty<ScrapeStep>(), s);
            }
            else
            {
                buffer.Add(s);
            }
        }
        if (buffer.Count > 0) { yield return new StepSegment(buffer.ToList(), null); }
    }

    private static IReadOnlyList<string> ParseAllowList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<string>(); }
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return Array.Empty<string>(); }
    }

    private static Dictionary<string, string> DecryptVariables(IEnumerable<ScrapeVariable> vars, ISecretProtector protector)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in vars)
        {
            if (string.IsNullOrEmpty(v.ValueEncrypted)) { continue; }
            if (v.IsSecret)
            {
                try { dict[v.Name] = protector.Unprotect(v.ValueEncrypted); } catch { /* omite la corrupta */ }
            }
            else { dict[v.Name] = v.ValueEncrypted; }
        }
        return dict;
    }

    private static string? FirstError(BrowserResultMsg msg) =>
        msg.Error ?? msg.Results.FirstOrDefault(r => !r.Ok)?.Error;

    private static string NewCorr() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>Primer URL navegado del tramo, para el resumen de la bitacora (o vacio).</summary>
    private static string NavUrlOf(CompiledFlow compiled)
    {
        var url = compiled.Actions.FirstOrDefault(a => a.Kind == BrowserActionKind.Navigate)?.Url;
        return string.IsNullOrEmpty(url) ? "" : $"{url} - ";
    }
}
