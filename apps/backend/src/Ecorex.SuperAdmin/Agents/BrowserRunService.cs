using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Automatizaciones.ConciliacionDian;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.Rules;
using Ecorex.Application.Scraping;
using Ecorex.Application.Storage;
using Ecorex.Contracts.Agent;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Auth;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>Lo que devuelve "Ejecutar ahora": si se despacho, a que corrida corresponde, y si quedo
/// esperando al agente o fallo antes de salir.</summary>
public sealed record BrowserRunResult(bool Dispatched, Guid? RunId, string? CorrelationId, bool Offline, string? Error);

/// <summary>Una variable "Pedir al ejecutar" de un flujo: <see cref="Prompt"/> es la pregunta (o el nombre) y
/// <see cref="DefaultValue"/> el valor guardado, que se muestra prellenado.</summary>
public sealed record FlowAskVariable(Guid FlowId, string FlowName, string Name, string Prompt, string DefaultValue);

/// <summary>Un tic de progreso de un flujo corrido paso a paso (para mostrar EN VIVO lo que hace la extraccion).
/// <see cref="Done"/>=false lo reporta justo ANTES de correr el paso (el UI lo pinta "en curso"); =true DESPUES,
/// con el resultado y la mini-captura. <see cref="FlowIndex"/>/<see cref="FlowCount"/> situan el flujo en la tanda;
/// <see cref="StepIndex"/>/<see cref="StepCount"/>, el paso en el flujo.</summary>
public sealed record FlowStepProgress(
    int FlowIndex, int FlowCount, string FlowName,
    int StepIndex, int StepCount, string StepName,
    bool Done, bool Ok, string? ScreenshotBase64, string? Detail);

/// <summary>Resultado SINCRONO de ejecutar UN paso en modo "paso a paso" (sesion viva): si salio bien, si
/// el agente estaba offline, el error, una captura de la pagina (base64 PNG), el valor devuelto por el paso
/// (texto/JSON del Eval, o el token leido en un paso OTP), cuantas filas se ingirieron, y el resumen.</summary>
public sealed record StepRunResult(
    bool Ok, bool Offline, string? Error, string? ScreenshotBase64, string? Value,
    int Inserted, int Updated, int Deleted, string? Detail);

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

    /// <summary>Corre el flujo PASO A PASO de forma sincrona (misma via que el modo paso a paso: una sola sesion
    /// viva, intercepta OTP, etc.) y REPORTA el progreso por <paramref name="progress"/> antes y despues de cada
    /// paso (nombre, indice, ok, captura PNG del paso). Pensado para que el conciliador muestre EN VIVO que esta
    /// haciendo la extraccion (como el modulo de Extraccion de datos) con mini-capturas. <paramref name="flowIndex"/>
    /// / <paramref name="flowCount"/> situan este flujo dentro de una tanda de varios. Nota: el paso de descarga del
    /// dron arranca descargas en 2do plano (sigue bajando tras cerrar el paso).</summary>
    Task<BrowserRunResult> RunFlowStepwiseAsync(Guid flowId, Guid tenantId, int flowIndex, int flowCount,
        IProgress<FlowStepProgress>? progress, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? overrides = null);

    /// <summary>Variables marcadas "Pedir al ejecutar" (no secretas) de los flujos dados, en el orden de los
    /// flujos, con su valor guardado como valor por defecto. El modulo que dispara los flujos las pregunta y
    /// pasa las respuestas como <c>overrides</c> a <see cref="RunFlowStepwiseAsync"/>.</summary>
    Task<IReadOnlyList<FlowAskVariable>> GetAskVariablesAsync(IReadOnlyList<Guid> flowIds, Guid tenantId,
        CancellationToken ct = default);

    /// <summary>Ejecuta UN solo paso del flujo contra la SESION VIVA del agente (modo "paso a paso"), de forma
    /// SINCRONA: despacha el paso con la clave de sesion del flujo (el agente mantiene el navegador abierto y
    /// reusa la pagina del paso anterior), ingiere lo que extraiga, lo registra en la bitacora y devuelve el
    /// resultado para pintarlo en la UI. Pensado para encadenar pasos a mano (login -> OTP -> continuar).</summary>
    Task<StepRunResult> RunStepNowAsync(Guid flowId, Guid stepId, Guid tenantId, CancellationToken ct = default,
        Action<string>? live = null);

    /// <summary>Ejecuta un BUCLE ForEach (motor del dron, Ola 2): recorre el array del contexto que indica el
    /// paso <paramref name="loopStepId"/> (su LoopOverVar, p.ej. LISTADO.compras) y, por cada fila, corre el
    /// cuerpo del bucle (desde ese paso hasta el que tenga IsLoopEnd) sobre la MISMA sesion viva, bindeando la
    /// fila como ROW; ingiere lo que los pasos marquen con IngestPath. Sincrono; para "consumir una por una".</summary>
    Task<StepRunResult> RunLoopNowAsync(Guid flowId, Guid loopStepId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Cierra la sesion viva del flujo en el agente (la ventana que el paso a paso mantenia abierta) y
    /// olvida las variables de sesion (p.ej. el token OTP leido). Best-effort.</summary>
    Task<StepRunResult> CloseStepSessionAsync(Guid flowId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Cierra corridas que quedaron "Running" colgadas (p.ej. el servidor se reinicio a mitad).
    /// Lo llama el worker; los timeouts por accion los maneja el canal, esto es la red de seguridad.</summary>
    Task SweepAsync(CancellationToken ct = default);

    /// <summary>Backfill del historico NEWTON: rellena fecha/NIT/totales de las filas sin enriquecer re-parseando
    /// el {CUFE}.xml ya guardado en el gestor de archivos (blob), sin re-descargar de NEWTON.</summary>
    Task<(int Enriquecidas, int SinArchivo, int Total)> BackfillNewtonDesdeBlobAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed class BrowserRunService(
    IAgentRegistry registry,
    IBrowserActionChannel channel,
    IFileActionChannel fileChannel,
    IServiceScopeFactory scopeFactory,
    IAgentActivityLog activity,
    ILogger<BrowserRunService> log,
    TimeProvider? clock = null) : IBrowserRunService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Cliente HTTP compartido para las ingestas por API (p.ej. NEWTON). El servicio es Singleton,
    /// asi que se reusa una sola instancia (evita agotar sockets). Timeout amplio: la descarga de PDFs puede tardar.</summary>
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>Una corrida Running mas vieja que esto se da por colgada (el canal ya habria fallado sus
    /// acciones; esto solo limpia lo que quedo tras un reinicio del proceso).</summary>
    private static readonly TimeSpan StaleRunAge = TimeSpan.FromMinutes(20);

    /// <summary>Corridas VIVAS en este proceso (correlationId -> anidamiento). El barrido de colgadas las
    /// salta: un flujo largo (DRON DIAN: espera de 19 min + procesar) supera StaleRunAge sin estar colgado.
    /// Tras un reinicio el set nace vacio, asi que las que de verdad quedaron huerfanas si se cierran.</summary>
    private readonly ConcurrentDictionary<string, int> _activeRuns = new(StringComparer.Ordinal);

    private void TrackRun(string runCorr) => _activeRuns.AddOrUpdate(runCorr, 1, (_, n) => n + 1);

    private void UntrackRun(string runCorr)
    {
        if (_activeRuns.AddOrUpdate(runCorr, 0, (_, n) => n - 1) <= 0) { _activeRuns.TryRemove(runCorr, out _); }
    }

    /// <summary>Variables de SESION del modo paso a paso, por flujo: valores efimeros producidos durante el
    /// stepping (p.ej. el token que leyo un paso OTP) que se superponen a las variables del flujo para los
    /// pasos siguientes, SIN persistirlos en BD. Se limpian al cerrar la sesion. Singleton -> estado vivo
    /// entre llamadas (cada paso es una invocacion aparte).</summary>
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, string>> _stepSessionVars = new();

    /// <summary>Clave de sesion viva del navegador para el stepping de un flujo (el agente reusa el mismo
    /// perfil/ventana entre pasos). Estable por flujo.</summary>
    // Espera "con progreso": sondea en bucle el fin (#dron-fin, via step.Selector) y el contador que el
    // motor del dron publica en #dron-prog, reportando el texto al modal del conciliador (callback live).
    // Firma cada sondeo igual que el compilador (Eval firmado) y corre sobre la MISMA sesion viva del paso.
    // Un sondeo que falle no corta la espera. Termina al ver el fin (Ok) o al vencer WaitMs (benigno: el
    // flujo sigue a Procesar con lo que haya en disco).
    private async Task<StepRunResult> RunWaitConProgresoAsync(
        IApplicationDbContext db, Guid flowId, Guid tenantId, ScrapeStep step,
        DataClient client, string? secret, Dictionary<string, string> vars,
        Action<string> live, CancellationToken ct)
    {
        var sessionKey = StepSessionKeyFor(flowId);
        var folder = DownloadFolderFrom(vars);
        var selLiteral = JsonSerializer.Serialize(step.Selector);
        // Cuerpo de funcion (como los pasos nativos): devuelve un JSON {fin, prog}. Se envuelve y firma igual.
        // Ademas de {fin, prog} se leen el banner vivo del motor (#diag-bot) y su resumen por ventana
        // (#diag-sum): son la UNICA traza de por que se corto una ventana (sesion caida, rechazos, re-login) y
        // se perdian al cerrar el navegador. Se guardan en el resultado del paso.
        var body = "function t(i){var e=document.getElementById(i);return e?(''+e.textContent):'';}"
            + "return JSON.stringify({fin: !!document.querySelector(" + selLiteral
            + "), prog:t('dron-prog'), ban:t('diag-bot'), sum:t('diag-sum')});";
        var wrapped = "(function(){\n" + body + "\n})()";

        var waitMs = step.WaitMs is > 0 ? step.WaitMs.Value : 600000;
        var deadline = _clock.GetUtcNow().AddMilliseconds(waitMs);
        string? lastProg = null;
        string? lastSum = null;
        string? lastBan = null;
        var eventos = new List<string>(); // eventos criticos del motor, en orden, sin repetir consecutivos

        while (_clock.GetUtcNow() < deadline)
        {
            if (!registry.IsOnline(client.ClientId))
            {
                return new StepRunResult(false, true, "El agente quedo offline durante la espera.", null, null, 0, 0, 0, null);
            }

            string? value = null;
            try
            {
                var corr = NewCorr();
                var sig = secret is null ? null : AgentSign.SignJs(secret, corr, wrapped);
                var action = new BrowserAction(BrowserActionKind.Eval, Script: wrapped, Signature: sig);
                var req = new BrowserRequestMsg(corr, tenantId.ToString(), new List<BrowserAction> { action },
                    SessionKey: sessionKey, KeepAlive: true, DownloadFolder: folder);
                var res = await channel.ExecuteAsync(client.ClientId, req, TimeSpan.FromSeconds(30), ct);
                value = res.Results.FirstOrDefault(r => r.Kind == BrowserActionKind.Eval)?.Value
                    ?? res.Results.FirstOrDefault()?.Value;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch { /* un sondeo fallido no mata la espera */ }

            var fin = false;
            if (!string.IsNullOrWhiteSpace(value))
            {
                try
                {
                    // El Eval (WebView2 ExecuteScriptAsync) entrega el resultado JSON-encoded SIN decodificar:
                    // si llega como literal de cadena ("...{...}..."), se decodifica UNA vez para obtener el
                    // objeto real {fin,prog}. Si ya viene como objeto, se parsea directo.
                    var raw = value!.Trim();
                    if (raw.Length > 1 && raw[0] == '"')
                    {
                        try { raw = JsonSerializer.Deserialize<string>(raw) ?? raw; } catch { /* no era literal */ }
                    }
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("fin", out var f))
                    {
                        fin = f.ValueKind == JsonValueKind.True
                            || (f.ValueKind == JsonValueKind.String && string.Equals(f.GetString(), "true", StringComparison.OrdinalIgnoreCase));
                    }
                    var p = root.TryGetProperty("prog", out var pe) ? pe.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(p) && p != lastProg) { live(p!); lastProg = p; }
                    var s = root.TryGetProperty("sum", out var se) ? se.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(s)) { lastSum = s; }
                    var b = root.TryGetProperty("ban", out var be) ? be.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(b) && b != lastBan)
                    {
                        lastBan = b;
                        if (EsEventoCriticoDron(b!) && (eventos.Count == 0 || eventos[^1] != b) && eventos.Count < 40)
                        {
                            eventos.Add(b!.Length > 160 ? b[..160] : b);
                        }
                    }
                }
                catch { /* value no-JSON: se ignora este sondeo */ }
            }

            if (fin)
            {
                string? shot = null;
                try
                {
                    var corr2 = NewCorr();
                    var req2 = new BrowserRequestMsg(corr2, tenantId.ToString(),
                        new List<BrowserAction> { new BrowserAction(BrowserActionKind.Screenshot, Screenshot: true) },
                        SessionKey: sessionKey, KeepAlive: true, DownloadFolder: folder);
                    var res2 = await channel.ExecuteAsync(client.ClientId, req2, TimeSpan.FromSeconds(30), ct);
                    shot = res2.Results.LastOrDefault(r => !string.IsNullOrEmpty(r.ScreenshotBase64))?.ScreenshotBase64;
                }
                catch { /* la captura final es opcional */ }
                // El resumen por ventana va PRIMERO (el detalle se recorta a 600); la traza completa, al log.
                var traza = TrazaDron(lastSum, eventos);
                log.LogInformation("[DRON-TRAZA] flow={Flow} {Prog} {Traza}", flowId, lastProg, traza);
                var okDet = string.IsNullOrWhiteSpace(traza)
                    ? (lastProg is null ? "Descargas finalizadas." : $"Descargas finalizadas ({lastProg}).")
                    : "Descargas finalizadas." + traza;
                await RecordStepRunAsync(db, flowId, step.Name, true, 0, 0, 0, okDet, ct);
                return new StepRunResult(true, false, null, shot, null, 0, 0, 0, okDet);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(4), ct); }
            catch (OperationCanceledException) { break; }
        }

        // Timeout: nunca aparecio el selector de fin. Benigno: el flujo sigue a Procesar con lo que haya en disco.
        var toTraza = TrazaDron(lastSum, eventos);
        log.LogInformation("[DRON-TRAZA] flow={Flow} timeout {Prog} {Traza}", flowId, lastProg, toTraza);
        var toDet = "Espera agotada (timeout)." + toTraza;
        await RecordStepRunAsync(db, flowId, step.Name, false, 0, 0, 0, toDet, ct);
        return new StepRunResult(false, false, toDet, null, null, 0, 0, 0, toDet);
    }

    /// <summary>Eventos del banner del motor de descarga que explican un corte (los "OK"/"intento" no).</summary>
    private static bool EsEventoCriticoDron(string ban) =>
        System.Text.RegularExpressions.Regex.IsMatch(ban,
            "SIN SESION|RE-LOGIN|re-login|RECHAZO|RE-BUSCAR|re-Buscar agotado|agotada|vacia|TIMEOUT|token NO regenera|fetch-err|tope MAX|PARTE VENTANA|iniciando|FIN ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Resumen por ventana + eventos criticos, para el detalle del paso (la traza del navegador se
    /// pierde al cerrarlo). Recortado para no inflar la bitacora.</summary>
    private static string TrazaDron(string? sum, List<string> eventos)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(sum)) { sb.Append(' ').Append(sum!.Length > 900 ? sum[..900] : sum); }
        if (eventos.Count > 0) { sb.Append(" [EVENTOS] ").Append(string.Join(" | ", eventos)); }
        var r = sb.ToString();
        return r.Length > 2500 ? r[..2500] : r;
    }

    private static string StepSessionKeyFor(Guid flowId) => $"stepflow-{flowId:N}";

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

            // Flujos con bucles / pasos de servidor (p.ej. DRON PLATAFORMAS INGENIOS): la corrida compilada no
            // los recorre, asi que van por la corrida POR PASOS, en segundo plano (misma semantica de
            // "despachado" para la UI y la programacion). Si el agente no esta en linea, sigue el camino de
            // abajo, que parquea la corrida como offline.
            if (NeedsStepwise(flow) && flow.ClientId is Guid cpk)
            {
                var cli = await db.DataClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cpk && c.IsActive, ct);
                if (cli is not null && registry.IsOnline(cli.ClientId))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await RunStepwiseCoreAsync(flowId, tenantId, 0, 1, null, trigger, runCorr,
                                freshSession: true, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            log.LogError(ex, "[NAV-RUN] fallo la corrida por pasos corr={Corr} flow={Flow}", runCorr, flowId);
                        }
                    }, CancellationToken.None);
                    log.LogInformation("[NAV-RUN] corrida por pasos lanzada corr={Corr} flow={Flow}", runCorr, flowId);
                    return new BrowserRunResult(true, null, runCorr, false, null);
                }
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

    public Task<BrowserRunResult> RunFlowStepwiseAsync(Guid flowId, Guid tenantId, int flowIndex, int flowCount,
        IProgress<FlowStepProgress>? progress, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? overrides = null)
        => RunStepwiseCoreAsync(flowId, tenantId, flowIndex, flowCount, progress, ImportRunTrigger.Manual, NewCorr(),
            freshSession: false, ct, overrides);

    public async Task<IReadOnlyList<FlowAskVariable>> GetAskVariablesAsync(IReadOnlyList<Guid> flowIds, Guid tenantId,
        CancellationToken ct = default)
    {
        if (flowIds.Count == 0) { return Array.Empty<FlowAskVariable>(); }
        using var scope = scopeFactory.CreateScope();
        using (AmbientTenantContext.Begin(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var ids = flowIds.Distinct().ToList();
            var rows = await db.ScrapeVariables.AsNoTracking()
                .Where(v => ids.Contains(v.FlowId) && v.AskOnRun && !v.IsSecret)
                .Select(v => new { v.FlowId, FlowName = v.Flow!.Name, v.Name, v.Prompt, v.ValueEncrypted })
                .ToListAsync(ct);
            // En el orden de los flujos (como se ejecutan) y por nombre dentro de cada flujo.
            return rows
                .OrderBy(r => ids.IndexOf(r.FlowId)).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(r => new FlowAskVariable(r.FlowId, r.FlowName, r.Name,
                    string.IsNullOrWhiteSpace(r.Prompt) ? r.Name : r.Prompt!, r.ValueEncrypted ?? ""))
                .ToList();
        }
    }

    /// <summary>
    /// true si el flujo necesita la corrida POR PASOS (no la compilada lineal): tiene bucles (LoopOverVar) o
    /// pasos que corren en el servidor dentro del flujo (Crear actividad). La compilada no recorre bucles ni
    /// ingiere por IngestPath, asi que "Ejecutar ahora" y la programacion de esos flujos no procesaban nada.
    /// </summary>
    // Espera tras un paso en la corrida completa por pasos: valores < 100 son los "segundos" heredados del dron
    // legacy y se ignoran (no son ms reales); tope de 2 minutos por paso.
    internal const int MinPostStepWaitMs = 100;

    /// <summary>Nombre de la variable del flujo que limita cuantas filas consume un bucle (simulacion/pruebas).</summary>
    internal const string LoopRowCapVar = "TopeFilasBucle";

    /// <summary>Tope de filas por bucle desde la variable del flujo <see cref="LoopRowCapVar"/>; 0 = sin tope.</summary>
    internal static int LoopRowCap(IReadOnlyDictionary<string, string> vars)
        => vars.TryGetValue(LoopRowCapVar, out var raw) && int.TryParse((raw ?? "").Trim(), out var n) && n > 0 ? n : 0;
    internal const int MaxPostStepWaitMs = 120_000;

    internal static bool NeedsStepwise(ScrapeFlow flow)
        => flow.Steps.Any(s => !string.IsNullOrWhiteSpace(s.LoopOverVar)
            || s.Kind is ScrapeStepKind.CrearActividad or ScrapeStepKind.Notificar);

    /// <summary>
    /// Pasos que consume el BUCLE que empieza en <paramref name="startIdx"/>: el cuerpo (hasta el primer
    /// IsLoopEnd, inclusive) y sus pasos de avance de pagina (IsPageNext despues del inicio y antes del
    /// siguiente inicio de bucle). Misma regla que <see cref="RunLoopNowAsync"/>; la corrida por pasos no
    /// los vuelve a correr sueltos.
    /// </summary>
    internal static HashSet<Guid> LoopMemberIds(IReadOnlyList<ScrapeStep> ordered, int startIdx)
    {
        var ids = new HashSet<Guid>();
        var start = ordered[startIdx];
        for (var i = startIdx; i < ordered.Count; i++)
        {
            ids.Add(ordered[i].Id);
            if (ordered[i].IsLoopEnd) { break; }
        }
        var nextLoopStartOrder = ordered
            .Where(s => !string.IsNullOrWhiteSpace(s.LoopOverVar) && s.Order > start.Order)
            .Select(s => (int?)s.Order).Min();
        foreach (var s in ordered.Where(s => s.IsPageNext && s.Order > start.Order
            && (nextLoopStartOrder is null || s.Order < nextLoopStartOrder)))
        {
            ids.Add(s.Id);
        }
        return ids;
    }

    private async Task<BrowserRunResult> RunStepwiseCoreAsync(Guid flowId, Guid tenantId, int flowIndex, int flowCount,
        IProgress<FlowStepProgress>? progress, ImportRunTrigger trigger, string runCorr, bool freshSession,
        CancellationToken ct, IReadOnlyDictionary<string, string>? overrides = null)
    {
        TrackRun(runCorr);
        // Corrida completa nueva (Ejecutar ahora / programacion): sin salidas capturadas de corridas anteriores,
        // para que un bucle nunca consuma un LISTADO viejo si el paso que lo lee falla.
        if (freshSession) { _stepSessionVars.TryRemove(flowId, out _); }
        // Valores SOLO de esta corrida (variables "Pedir al ejecutar" que el usuario respondio, o datos que
        // inyecta el modulo que dispara el flujo): van a la capa de sesion -la que ya se superpone a las
        // variables del flujo en cada paso- y se retiran al terminar, para no contaminar corridas/pasos
        // posteriores. Nunca se persisten en BD.
        var applied = new List<string>();
        if (overrides is { Count: > 0 })
        {
            var session = _stepSessionVars.GetOrAdd(flowId, _ => new ConcurrentDictionary<string, string>(StringComparer.Ordinal));
            foreach (var (k, v) in overrides)
            {
                if (string.IsNullOrWhiteSpace(k)) { continue; }
                session[k] = v ?? "";
                applied.Add(k);
            }
        }
        try
        {
            return await RunStepwiseCoreTrackedAsync(flowId, tenantId, flowIndex, flowCount, progress, trigger,
                runCorr, freshSession, ct);
        }
        finally
        {
            UntrackRun(runCorr);
            if (applied.Count > 0 && _stepSessionVars.TryGetValue(flowId, out var s))
            {
                foreach (var k in applied) { s.TryRemove(k, out _); }
            }
        }
    }

    private async Task<BrowserRunResult> RunStepwiseCoreTrackedAsync(Guid flowId, Guid tenantId, int flowIndex,
        int flowCount, IProgress<FlowStepProgress>? progress, ImportRunTrigger trigger, string runCorr,
        bool freshSession, CancellationToken ct)
    {
        var firedAt = _clock.GetUtcNow();
        // (La limpieza de la sesion en corridas nuevas y los overrides se aplican en RunStepwiseCoreAsync.)
        using var scope = scopeFactory.CreateScope();
        using (AmbientTenantContext.Begin(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var runLog = scope.ServiceProvider.GetRequiredService<IScrapeFlowRunLog>();

            var flow = await db.ScrapeFlows.Include(f => f.Steps).FirstOrDefaultAsync(f => f.Id == flowId, ct);
            if (flow is null) { return new BrowserRunResult(false, null, runCorr, false, "El flujo no existe o no es de este tenant."); }
            var flowName = flow.Name;
            var steps = flow.Steps.OrderBy(s => s.Order).ToList();
            var runId = await runLog.OpenAsync(flowId, trigger, firedAt, runCorr, steps.Count, ct);

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
            if (steps.Count == 0)
            {
                await runLog.FailAsync(runId, "El flujo no tiene pasos que ejecutar.", ct);
                return new BrowserRunResult(false, runId, runCorr, false, "El flujo no tiene pasos que ejecutar.");
            }
            if (!registry.IsOnline(client.ClientId))
            {
                await runLog.MarkOfflineAsync(runId, "El agente asignado no estaba en linea.", ct);
                return new BrowserRunResult(false, runId, runCorr, true, "El agente no esta en linea.");
            }

            int ins = 0, upd = 0, del = 0;
            var notas = new List<string>();
            var consumidos = new HashSet<Guid>(); // pasos que ya corrio un bucle (cuerpo + avance de pagina)
            for (var i = 0; i < steps.Count; i++)
            {
                var paso = steps[i];
                if (consumidos.Contains(paso.Id)) { continue; }
                progress?.Report(new FlowStepProgress(flowIndex, flowCount, flowName, i, steps.Count, paso.Name, false, false, null, null));

                // Progreso EN VIVO dentro del paso (p.ej. "procesando 3/15"): los pasos largos (Procesar
                // archivos / Ingesta NEWTON) lo llaman por documento; se reenvia como FlowStepProgress no-Done
                // con el texto en Detail, para que el modal del conciliador muestre el contador.
                var stepIdx = i;
                Action<string> stepLive = txt => progress?.Report(new FlowStepProgress(
                    flowIndex, flowCount, flowName, stepIdx, steps.Count, paso.Name, false, false, null, txt));

                StepRunResult r;
                try
                {
                    // Inicio de bucle: recorre TODAS las filas (y paginas) con su cuerpo; luego esos pasos no se
                    // corren sueltos. Sin bucles (p.ej. los drones DIAN) el camino es el de siempre.
                    if (!string.IsNullOrWhiteSpace(paso.LoopOverVar))
                    {
                        consumidos.UnionWith(LoopMemberIds(steps, i));
                        r = await RunLoopNowAsync(flowId, paso.Id, tenantId, ct);
                    }
                    else { r = await RunStepNowAsync(flowId, paso.Id, tenantId, ct, stepLive); }
                }
                catch (Exception ex) { r = new StepRunResult(false, false, ex.Message, null, null, 0, 0, 0, ex.Message); }
                ins += r.Inserted; upd += r.Updated; del += r.Deleted;

                progress?.Report(new FlowStepProgress(flowIndex, flowCount, flowName, i, steps.Count, paso.Name, true, r.Ok, r.ScreenshotBase64, r.Error ?? r.Detail));

                if (r.Offline)
                {
                    await runLog.CloseAsync(runCorr, false, ins, upd, del, $"El agente quedo offline en el paso '{paso.Name}'.", ct);
                    return new BrowserRunResult(false, runId, runCorr, true, "El agente quedo offline durante la extraccion.");
                }
                // El OTP es duro: sin token lo que sigue es inutil -> abortar el flujo.
                if (paso.Kind == ScrapeStepKind.LeerCorreoOtp && !r.Ok)
                {
                    var err = r.Error ?? r.Detail ?? "no se obtuvo el token";
                    await runLog.CloseAsync(runCorr, false, ins, upd, del, $"Paso '{paso.Name}': {err}.", ct);
                    return new BrowserRunResult(false, runId, runCorr, false, $"{paso.Name}: {err}");
                }
                // Pasos guarda/driver (Exploracion/Variable sin filas) devuelven Ok=false de forma benigna: se anota y sigue.
                if (!r.Ok) { notas.Add(paso.Name); }

                // Corrida completa nueva: respeta la espera del paso (WaitMs, en ms) antes del siguiente. El agente
                // corre el JS y vuelve de inmediato; si el script dispara un postback (filtrar, consultar, login), sin
                // esta pausa el paso siguiente leia la pagina VIEJA (0 filas). En el paso a paso manual la pausa la
                // ponia el operador entre clics. Los bucles no pasan por aqui (ya funcionan con su propio ritmo) y la
                // corrida del conciliador DIAN (freshSession=false) queda exactamente igual.
                if (freshSession && paso.Kind != ScrapeStepKind.Wait && string.IsNullOrWhiteSpace(paso.LoopOverVar)
                    && paso.WaitMs is int espera && espera >= MinPostStepWaitMs)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(espera, MaxPostStepWaitMs)), ct);
                }
            }

            var det = $"Flujo ejecutado paso a paso ({steps.Count} pasos)"
                + (notas.Count > 0 ? $"; sin filas en: {string.Join(", ", notas)}" : "") + ".";
            // Corrida completa nueva (Ejecutar ahora / programacion): no dejar la ventana del agente abierta.
            if (freshSession)
            {
                try { await CloseStepSessionAsync(flowId, tenantId, ct); } catch { /* best-effort */ }
            }
            await runLog.CloseAsync(runCorr, true, ins, upd, del, det, ct);
            log.LogInformation("[NAV-RUN] stepwise OK corr={Corr} flow={Flow} pasos={N}", runCorr, flowId, steps.Count);
            return new BrowserRunResult(true, runId, runCorr, false, det);
        }
    }

    // ---- Modo "paso a paso" (sesion viva): un paso a la vez, sincrono, sobre la MISMA ventana del agente ----

    public async Task<StepRunResult> RunStepNowAsync(Guid flowId, Guid stepId, Guid tenantId, CancellationToken ct = default,
        Action<string>? live = null)
    {
        using var scope = scopeFactory.CreateScope();
        using (AmbientTenantContext.Begin(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();

            var flow = await db.ScrapeFlows.Include(f => f.Steps).Include(f => f.Variables)
                .FirstOrDefaultAsync(f => f.Id == flowId, ct);
            if (flow is null) { return Fail("El flujo no existe o no es de este tenant."); }
            var step = flow.Steps.FirstOrDefault(s => s.Id == stepId);
            if (step is null) { return Fail("El paso no existe en este flujo."); }

            if (flow.ClientId is not Guid clientPk)
            {
                return Fail("El flujo no tiene un agente asignado.");
            }
            var client = await db.DataClients.FirstOrDefaultAsync(c => c.Id == clientPk && c.IsActive, ct);
            if (client is null) { return Fail("El agente asignado no existe o esta inactivo."); }
            if (!registry.IsOnline(client.ClientId))
            {
                return new StepRunResult(false, true, "El agente asignado no esta en linea.", null, null, 0, 0, 0, null);
            }

            string? secret = null;
            if (client.ClientSecretEncrypted is not null)
            {
                try { secret = protector.Unprotect(client.ClientSecretEncrypted); } catch { /* ilegible */ }
            }

            // Variables del flujo + las de sesion (overlay efimero: salidas capturadas de pasos anteriores y
            // el token que leyo un paso OTP). El contexto resuelve {{ruta}}/@@ruta@@ con acceso por punto/indice
            // a los JSON capturados (motor del dron, Ola 1).
            var vars = DecryptVariables(flow.Variables, protector);
            if (_stepSessionVars.TryGetValue(flowId, out var session))
            {
                foreach (var (k, v) in session) { vars[k] = v; }
            }
            var ctx = new ScrapeRunContext(vars);

            // Paso "Leer token de correo": NO va al navegador. Lee el OTP por IMAP y lo deja como variable de
            // sesion para que los pasos siguientes lo sustituyan como {{NOMBRE}}.
            if (step.Kind == ScrapeStepKind.LeerCorreoOtp)
            {
                return await RunOtpStepAsync(scope, db, flowId, tenantId, step, ct);
            }

            // Paso "Procesar archivos descargados": NO va al navegador. Lee la carpeta de descargas, parsea
            // cada archivo (DIAN UBL) y vuelca a la fuente Bot sin duplicar (CUFE); opcional auto-importar.
            if (step.Kind == ScrapeStepKind.ProcesarArchivos)
            {
                return await RunProcesarArchivosStepAsync(scope, db, flowId, tenantId, step, vars, ct, live);
            }

            // Paso "Ingesta NEWTON" (API del proveedor de facturacion electronica): NO va al navegador.
            // Autentica con Auth-Token, lista los documentos recibidos y DESCARGA cada {CUFE}.xml / {CUFE}_newton.pdf
            // a su carpeta propia. Fase 1: solo descarga (ProcesarArchivos / fase posterior aterriza en el modulo).
            if (step.Kind == ScrapeStepKind.IngestaNewton)
            {
                return await RunIngestaNewtonStepAsync(db, flowId, tenantId, step, vars, ct, live);
            }

            // Paso "Cerrar navegador": NO compila JS. Cierra la sesion viva del flujo (CloseSession) reusando
            // el mismo camino que el boton de cerrar sesion. Util como ultimo paso para no dejar el WebView2 abierto.
            if (step.Kind == ScrapeStepKind.CerrarNavegador)
            {
                var closed = await CloseStepSessionAsync(flowId, tenantId, ct);
                await RecordStepRunAsync(db, flowId, step.Name, closed.Ok, 0, 0, 0, closed.Detail, ct);
                return closed;
            }

            // Paso "Esperar" con condicion por selector (p.ej. "Esperar fin de descargas" del dron DIAN):
            // en vez de UNA espera bloqueante, SONDEA en bucle el fin (#dron-fin) y el contador que el motor
            // publica en #dron-prog, reportando "descargando N" en vivo al modal (callback live). Solo aplica
            // en la corrida "Ejecutar ahora" (hay live); el paso a paso manual (live null) cae al camino
            // generico de una sola espera. Para otros Wait con selector es equivalente (sin #dron-prog no hay
            // texto en vivo, solo se espera el selector).
            if (step.Kind == ScrapeStepKind.Wait && !string.IsNullOrWhiteSpace(step.Selector) && live is not null)
            {
                return await RunWaitConProgresoAsync(db, flowId, tenantId, step, client, secret, vars, live, ct);
            }

            if (step.Kind == ScrapeStepKind.Ai)
            {
                return Fail("El paso de IA no se ejecuta en modo paso a paso; usa \"Ejecutar ahora\".");
            }

            // ENSAMBLADO -> Regla (Ola 4): no va al navegador; invoca la regla con el contexto de sesion.
            if (step.RuleId is not null || step.Kind == ScrapeStepKind.Ensamblado)
            {
                var rules = scope.ServiceProvider.GetRequiredService<IRulesEngine>();
                var (rok, rdetail, recs) = await InvokeRuleAsync(rules, step, ctx, ct);
                await RecordStepRunAsync(db, flowId, step.Name, rok, recs, 0, 0, rdetail, ct);
                return new StepRunResult(rok, false, rok ? null : rdetail, null, rdetail, recs, 0, 0, rdetail);
            }

            // Crear actividad: no va al navegador; crea la tarea del registro en contexto (sin duplicar).
            if (step.Kind == ScrapeStepKind.CrearActividad)
            {
                var ca = await ScrapeCrearActividad.RunAsync(scope.ServiceProvider, db, tenantId, step, ctx,
                    $"Dron {flow.Name}", ct);
                // La tarea queda en las variables de sesion para el paso siguiente (p.ej. Notificar).
                var sessCa = _stepSessionVars.GetOrAdd(flowId, _ => new ConcurrentDictionary<string, string>(StringComparer.Ordinal));
                foreach (var (k, v) in ScrapeCrearActividad.OutputVars(step, ca)) { sessCa[k] = v; }
                await RecordStepRunAsync(db, flowId, step.Name, ca.Ok, ca.Created ? 1 : 0, 0, 0, ca.Detail, ct);
                return new StepRunResult(ca.Ok, false, ca.Ok ? null : ca.Detail, null, ca.Detail, ca.Created ? 1 : 0, 0, 0, ca.Detail);
            }

            // Notificar: no va al navegador; avisa por el canal elegido que la tarea del contexto existe.
            if (step.Kind == ScrapeStepKind.Notificar)
            {
                var nt = await ScrapeNotificar.RunAsync(scope.ServiceProvider, db, tenantId, step, ctx, $"Dron {flow.Name}", ct);
                await RecordStepRunAsync(db, flowId, step.Name, nt.Ok, 0, 0, 0, nt.Detail, ct);
                return new StepRunResult(nt.Ok, false, nt.Ok ? null : nt.Detail, null, nt.Detail, 0, 0, 0, nt.Detail);
            }

            var sessionKey = StepSessionKeyFor(flowId);
            var corr = NewCorr();
            // Pre-sustituye el paso contra el contexto (rutas a las salidas capturadas); el compilador luego
            // solo firma. Se trabaja sobre un CLON para no mutar la entidad rastreada (no se persiste el JS).
            var prepared = SubstitutedClone(step, ctx);
            CompiledFlow compiled;
            try
            {
                compiled = ScrapeFlowCompiler.CompileSteps(new[] { prepared }, flow.ContainerId,
                    EmptyVars, corr, secret);
            }
            catch (ScrapeCompileException ex) { return Fail(ex.Message); }

            // Siempre se agrega una captura al final para VER en que quedo la pagina tras el paso (no afecta
            // los indices de los ExtractBinding, que apuntan a las acciones compiladas, antes de esta).
            var actions = compiled.Actions.Append(new BrowserAction(BrowserActionKind.Screenshot, Screenshot: true)).ToList();
            if (compiled.Actions.Count == 0)
            {
                return Fail("El paso no produjo ninguna accion de navegador.");
            }

            var ingest = scope.ServiceProvider.GetRequiredService<IRowIngestService>();
            var timeout = TimeSpan.FromSeconds(60 + actions.Sum(a => (a.WaitMs ?? 0) / 1000.0));
            var started = DateTimeOffset.UtcNow;
            int ins = 0, upd = 0, del = 0;
            try
            {
                var req = new BrowserRequestMsg(corr, tenantId.ToString(), actions, SessionKey: sessionKey, KeepAlive: true, DownloadFolder: DownloadFolderFrom(vars));
                var result = await channel.ExecuteAsync(client.ClientId, req, timeout, ct);

                var screenshot = result.Results.LastOrDefault(r => !string.IsNullOrEmpty(r.ScreenshotBase64))?.ScreenshotBase64;
                var firstErr = FirstError(result);

                if (!result.Ok)
                {
                    await activity.RecordAsync(new AgentActivityEntry(
                        tenantId, client.ClientId, null, AgentActivityKind.Browser, corr,
                        $"Paso a paso: {flow.Name} / {step.Name}", false, started, DateTimeOffset.UtcNow, firstErr));
                    await RecordStepRunAsync(db, flowId, step.Name, false, 0, 0, 0, firstErr, ct);
                    return new StepRunResult(false, false, firstErr ?? "El navegador reporto un error.",
                        screenshot, null, 0, 0, 0, firstErr);
                }

                // Ingesta de los pasos Extract (si este paso extraia filas).
                foreach (var bind in compiled.Extracts)
                {
                    var res = result.Results.FirstOrDefault(r => r.Index == bind.ActionIndex);
                    if (res is null || !res.Ok)
                    {
                        var msg = $"La extraccion no devolvio datos: {res?.Error ?? "sin resultado"}.";
                        await RecordStepRunAsync(db, flowId, step.Name, false, 0, 0, 0, msg, ct);
                        return new StepRunResult(false, false, msg, screenshot, null, 0, 0, 0, msg);
                    }
                    var rows = ScrapeRowIngest.ParseRows(res.Value);
                    var (i, u, d) = await ScrapeRowIngest.IngestAsync(ingest, db, bind.TargetContainerId, tenantId, bind.MappingJson, rows, ct);
                    ins += i; upd += u; del += d;
                }

                // Valor visible: lo que devolvio el paso (Eval/Html/ExtractReadable), recortado para la UI.
                var value = result.Results
                    .Where(r => r.Kind != BrowserActionKind.Screenshot && !string.IsNullOrEmpty(r.Value))
                    .Select(r => r.Value).FirstOrDefault();

                // Motor del dron (Ola 1): si el paso define OutputVar, se CAPTURA su salida en el contexto de
                // sesion para que los pasos siguientes la usen ({{OutputVar.ruta}} / @@OutputVar.ruta@@).
                if (!string.IsNullOrWhiteSpace(step.OutputVar))
                {
                    var captured = UnwrapEvalValue(value);
                    _stepSessionVars.GetOrAdd(flowId, _ => new ConcurrentDictionary<string, string>(StringComparer.Ordinal))
                        [step.OutputVar!.Trim()] = captured ?? string.Empty;
                }

                var detail = ins > 0 ? $"{ins} filas"
                    : string.IsNullOrWhiteSpace(step.OutputVar) ? "Paso ejecutado" : $"Paso ejecutado (capturado en {{{{{step.OutputVar!.Trim()}}}}})";

                await activity.RecordAsync(new AgentActivityEntry(
                    tenantId, client.ClientId, null, AgentActivityKind.Browser, corr,
                    $"Paso a paso: {flow.Name} / {step.Name}", true, started, DateTimeOffset.UtcNow, detail));
                await RecordStepRunAsync(db, flowId, step.Name, true, ins, upd, del, detail, ct);
                return new StepRunResult(true, false, null, screenshot, Shorten(value, 4000), ins, upd, del, detail);
            }
            catch (TimeoutException ex)
            {
                await RecordStepRunAsync(db, flowId, step.Name, false, 0, 0, 0, ex.Message, ct);
                return Fail(ex.Message);
            }
            catch (Exception ex)
            {
                await RecordStepRunAsync(db, flowId, step.Name, false, 0, 0, 0, ex.Message, ct);
                log.LogError(ex, "[NAV-STEP] fallo el paso {Step} del flujo {Flow}", stepId, flowId);
                return Fail(ex.Message);
            }
        }

        static StepRunResult Fail(string error) => new(false, false, error, null, null, 0, 0, 0, error);
    }

    /// <summary>Paso "Leer token de correo" dentro del paso a paso: lee el OTP por IMAP y lo guarda como
    /// variable de sesion del flujo (no persiste en BD). Registra el paso en la bitacora.</summary>
    private async Task<StepRunResult> RunOtpStepAsync(IServiceScope scope, IApplicationDbContext db, Guid flowId,
        Guid tenantId, ScrapeStep step, CancellationToken ct)
    {
        OtpStepConfig? cfg = null;
        if (!string.IsNullOrWhiteSpace(step.MappingJson))
        {
            try { cfg = JsonSerializer.Deserialize<OtpStepConfig>(step.MappingJson!); } catch { /* json viejo */ }
        }
        if (cfg?.MailboxId is not Guid mailboxId)
        {
            var msg = "El paso OTP no tiene un buzon configurado.";
            await RecordStepRunAsync(db, flowId, step.Name, false, 0, 0, 0, msg, ct);
            return new StepRunResult(false, false, msg, null, null, 0, 0, 0, msg);
        }

        var otp = scope.ServiceProvider.GetRequiredService<IOtpMailboxConfigService>();
        var regex = string.IsNullOrWhiteSpace(cfg.Regex) ? @"\b(\d{4,8})\b" : cfg.Regex!;
        var timeout = cfg.TimeoutSeconds > 0 ? cfg.TimeoutSeconds : 120;
        // Acota a correos recientes (el login acaba de dispararse en el paso anterior).
        var since = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10);
        var started = DateTimeOffset.UtcNow;
        OtpReadResult read;
        try { read = await otp.LeerTokenAsync(mailboxId, cfg.From, cfg.Subject, regex, timeout, since, ct); }
        catch (Exception ex) { read = new OtpReadResult(false, null, ex.Message); }

        if (!read.Ok || string.IsNullOrEmpty(read.Token))
        {
            var msg = read.Error ?? "No se encontro el token en el correo.";
            await RecordStepRunAsync(db, flowId, step.Name, false, 0, 0, 0, msg, ct);
            return new StepRunResult(false, false, msg, null, null, 0, 0, 0, msg);
        }

        var varName = string.IsNullOrWhiteSpace(cfg.Variable) ? "TOKEN" : cfg.Variable!.Trim();
        // El token/enlace puede venir codificado de dos formas segun como llegue el correo:
        //  - entidades HTML en el href (correo reenviado: ...AuthToken?pk=..&amp;rk=..) -> HtmlDecode
        //  - percent-encoding (enlace envuelto en SafeLinks de Outlook: url=https%3A%2F%2F..) -> UnescapeDataString
        // Con UrlDecode aplicamos ambos (en ese orden) para dejarlo listo para navegar.
        var tokenValue = cfg.UrlDecode
            ? Uri.UnescapeDataString(System.Net.WebUtility.HtmlDecode(read.Token!))
            : read.Token!;
        var session = _stepSessionVars.GetOrAdd(flowId, _ => new ConcurrentDictionary<string, string>(StringComparer.Ordinal));
        session[varName] = tokenValue;

        var detail = $"Token leido en {{{{{varName}}}}}: {tokenValue}";
        await RecordStepRunAsync(db, flowId, step.Name, true, 0, 0, 0, detail, ct);
        return new StepRunResult(true, false, null, null, tokenValue, 0, 0, 0, detail);
    }

    /// <summary>Config del paso "Procesar archivos descargados" (MappingJson). Todo opcional con defaults.</summary>
    private sealed record ProcesarArchivosConfig(
        string? Carpeta = null, string? Patron = "*.zip", string? Formato = "DianUbl",
        string? Destino = "ConciliacionBot", string? LlaveDedup = "CUFE",
        bool AutoImportar = true, bool MoverProcesados = true,
        // Espera a que la carpeta se "asiente" (sin archivos nuevos) antes de parsear, para no correr a mitad
        // de las descargas. Robusto por construccion (mira el filesystem, no depende de la señal del bucle).
        int EsperarAsentarSegundos = 30, int GraciaInicialSegundos = 150, int MaxEsperaSegundos = 900,
        // Sube el PDF (representacion grafica) y el XML de cada ZIP al gestor unificado (Azure Blob) por CUFE,
        // para poder verlos desde el modulo. Idempotente (no re-sube lo ya registrado).
        bool SubirABlob = true,
        // Fuerza el camino "via agente" (sub-agente Archivos) aunque el server VEA la carpeta en su propio
        // filesystem. Util para PROBAR en dev con una sola maquina (app y agente en la misma PC): sin esto el
        // server tomaria el camino directo porque la carpeta existe. En prod no hace falta (el server Linux no
        // ve la carpeta del agente Windows y el camino via agente se elige solo).
        bool ForzarViaAgente = false);

    /// <summary>Procesa (server-side) los archivos ya descargados: parsea cada ZIP (DIAN UBL), hace UPSERT por
    /// CUFE en la fuente Bot (sin duplicar), mueve los procesados y -si AutoImportar- los lleva al CCD del mes.
    /// La carpeta sale de la config o, por defecto, de la variable RutaDescargaBot del flujo.</summary>
    private async Task<StepRunResult> RunProcesarArchivosStepAsync(
        IServiceScope scope, IApplicationDbContext db, Guid flowId, Guid tenantId, ScrapeStep step,
        IReadOnlyDictionary<string, string> vars, CancellationToken ct, Action<string>? live = null)
    {
        ProcesarArchivosConfig cfg = new();
        if (!string.IsNullOrWhiteSpace(step.MappingJson))
        {
            try
            {
                cfg = JsonSerializer.Deserialize<ProcesarArchivosConfig>(step.MappingJson!,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? cfg;
            }
            catch { /* json viejo/invalido: defaults */ }
        }

        var carpeta = !string.IsNullOrWhiteSpace(cfg.Carpeta) ? ResolveVars(cfg.Carpeta!, vars)
            : (vars.TryGetValue("RutaDescargaBot", out var rd) && !string.IsNullOrWhiteSpace(rd) ? rd : null);
        if (string.IsNullOrWhiteSpace(carpeta))
        {
            var m = "No existe la carpeta de descargas: (sin configurar; define RutaDescargaBot o Carpeta).";
            await RecordStepRunAsync(db, flowId, step.Name, false, 0, 0, 0, m, ct);
            return new StepRunResult(false, false, m, null, null, 0, 0, 0, m);
        }
        var patron = string.IsNullOrWhiteSpace(cfg.Patron) ? "*.zip" : cfg.Patron!;
        var nuestroNit = vars.TryGetValue("NIT_EMPRESA", out var ne) ? ne.Trim() : "";

        // La carpeta de descargas puede vivir en la maquina del AGENTE (Windows) y no en el servidor
        // (Linux en prod). Discriminador robusto por construccion: si el servidor NO ve la carpeta en su
        // propio filesystem, fetchea los ZIP a traves del sub-agente Archivos (mismo parseo + upsert por
        // CUFE + blob). En local (app y agente en la misma PC) la carpeta SI existe -> camino directo de abajo,
        // salvo que ForzarViaAgente obligue el camino remoto (para probar en dev con una sola maquina).
        if (cfg.ForzarViaAgente || !Directory.Exists(carpeta))
        {
            return await RunProcesarArchivosViaAgenteAsync(db, flowId, tenantId, step, cfg, carpeta!, patron, nuestroNit, ct, live);
        }

        // Esperar a que las descargas se ASIENTEN: encuesta la carpeta hasta que el conteo no cambie durante
        // EsperarAsentarSegundos (y haya >=1), con una gracia inicial para que empiecen a llegar y un tope.
        // Robusto por construccion: mira el filesystem, no depende de la señal (fragil) del bucle del navegador.
        if (cfg.EsperarAsentarSegundos > 0)
        {
            const int intervalo = 5;
            int lastCount = -1, estableSeg = 0, total = 0;
            while (total < cfg.MaxEsperaSegundos)
            {
                var n = Directory.GetFiles(carpeta, patron).Length;
                if (n == lastCount) { estableSeg += intervalo; } else { estableSeg = 0; lastCount = n; }
                if (n > 0 && estableSeg >= cfg.EsperarAsentarSegundos) { break; }
                if (n == 0 && total >= cfg.GraciaInicialSegundos) { break; }
                await Task.Delay(TimeSpan.FromSeconds(intervalo), ct);
                total += intervalo;
            }
        }

        // Scope/DbContext FRESCO tras la espera: la conexion del scope original puede quedar stale durante una
        // descarga larga (timeout de Npgsql). El tenant ambiente sigue activo (filtro global + ITenantContext).
        using var dbScope = scopeFactory.CreateScope();
        var fdb = dbScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var archivos = Directory.GetFiles(carpeta, patron);
        if (archivos.Length == 0)
        {
            var m = "No hay archivos para procesar en la carpeta.";
            await RecordStepRunAsync(fdb, flowId, step.Name, true, 0, 0, 0, m, ct);
            return new StepRunResult(true, false, null, null, null, 0, 0, 0, m);
        }

        var procDir = Path.Combine(carpeta, "procesados");
        int nuevos = 0, actualizados = 0, fallidos = 0;
        var periodos = new HashSet<(int Anio, int Mes)>();
        var okFiles = new List<string>(); // archivos parseados OK: se mueven SOLO si SaveChanges confirma
        var cufePorZip = new List<(string Cufe, string Archivo)>(); // para subir el PDF/XML del zip por CUFE
        var sinCargar = new List<(string Nombre, string Motivo)>(); // documentos que NO quedaron completos

        var procIdx = 0;
        foreach (var archivo in archivos)
        {
            live?.Invoke($"procesando {++procIdx}/{archivos.Length}");
            DianDoc? doc;
            string? motivoParse = null;
            try { doc = DianUblParser.ParseZip(archivo, nuestroNit); }
            catch (Exception ex) { doc = null; motivoParse = ex.Message; }
            if (doc is null || string.IsNullOrWhiteSpace(doc.Cufe))
            {
                fallidos++;
                sinCargar.Add((Path.GetFileName(archivo),
                    "no se pudo leer como factura DIAN" + (motivoParse is null ? " (sin CUFE)" : ": " + motivoParse)));
                continue;
            }

            var existente = await fdb.ConciliacionDianBotDummies.FirstOrDefaultAsync(b => b.Cufe == doc.Cufe, ct);
            if (existente is null)
            {
                fdb.ConciliacionDianBotDummies.Add(new ConciliacionDianBotDummy
                {
                    TenantId = tenantId,
                    Cufe = doc.Cufe,
                    TipoDoc = doc.TipoDoc,
                    FechaDoc = doc.Fecha,
                    NombreEmisor = doc.NombreEmisor,
                    NitEmisor = doc.NitEmisor,
                    PrefijoFolio = doc.NumFactura,
                    IdCompra = "",
                    SubtotalBruto = doc.SubtotalBruto,
                    DescuentoComercial = doc.Descuento,
                    Subtotal = doc.SubtotalNeto,
                    Iva = doc.Iva,
                    TotalAntesRet = doc.TotalAntesRet,
                    RetencionFuente = doc.Retefuente,
                    RetencionIca = doc.ReteIca,
                    Total = doc.Total,
                    TipoPago = doc.TipoPago,
                    ProveedorTecnologico = "",
                });
                nuevos++;
            }
            else
            {
                existente.TipoDoc = doc.TipoDoc; existente.FechaDoc = doc.Fecha;
                existente.NombreEmisor = doc.NombreEmisor; existente.NitEmisor = doc.NitEmisor;
                existente.PrefijoFolio = doc.NumFactura; existente.SubtotalBruto = doc.SubtotalBruto;
                existente.DescuentoComercial = doc.Descuento; existente.Subtotal = doc.SubtotalNeto;
                existente.Iva = doc.Iva; existente.TotalAntesRet = doc.TotalAntesRet;
                existente.RetencionFuente = doc.Retefuente; existente.RetencionIca = doc.ReteIca;
                existente.Total = doc.Total; existente.TipoPago = doc.TipoPago;
                actualizados++;
            }
            periodos.Add((doc.Fecha.Year, doc.Fecha.Month));
            okFiles.Add(archivo);
            cufePorZip.Add((doc.Cufe, archivo));
        }
        await fdb.SaveChangesAsync(ct);

        // Mover a /procesados SOLO despues de confirmar (evita perder archivos si el guardado falla).
        if (cfg.MoverProcesados && okFiles.Count > 0)
        {
            try { Directory.CreateDirectory(procDir); } catch { /* best-effort */ }
            foreach (var archivo in okFiles)
            {
                try { File.Move(archivo, Path.Combine(procDir, Path.GetFileName(archivo)), overwrite: true); }
                catch { /* si no se puede mover, el dedup por CUFE evita duplicar en la proxima corrida */ }
            }
        }

        int importados = 0;
        if (cfg.AutoImportar)
        {
            var svc = dbScope.ServiceProvider.GetRequiredService<IConciliacionDianService>();
            foreach (var (anio, mes) in periodos)
            {
                var ccd = await fdb.ConciliacionDianDocumentos.FirstOrDefaultAsync(d => d.Anio == anio && d.Mes == mes, ct);
                Guid? docId = ccd?.Id;
                if (docId is null)
                {
                    var (nuevo, _) = await svc.CrearDocumentoAsync(anio, mes, ct);
                    docId = nuevo?.Id;
                }
                if (docId is Guid id) { importados += await svc.ImportarDesdeBotAsync(id, ct); }
            }
        }

        // Sube al gestor unificado (blob) el PDF (representacion grafica) y el XML de cada ZIP, por CUFE. Idempotente.
        int subidos = 0;
        if (cfg.SubirABlob && cufePorZip.Count > 0)
        {
            var files = dbScope.ServiceProvider.GetRequiredService<IFileStorageService>();
            foreach (var (cufe, archivoOrig) in cufePorZip)
            {
                var nombreZip = Path.GetFileName(archivoOrig);
                // Si no se pudo mover, el ZIP sigue en la carpeta de entrada: subirlo desde donde este.
                var zipFinal = cfg.MoverProcesados ? Path.Combine(procDir, nombreZip) : archivoOrig;
                if (!File.Exists(zipFinal)) { zipFinal = archivoOrig; }
                if (!File.Exists(zipFinal)) { sinCargar.Add((nombreZip, "el ZIP desaparecio antes de subir PDF/XML")); continue; }
                byte[] zipBytes;
                try { zipBytes = await File.ReadAllBytesAsync(zipFinal, ct); }
                catch (Exception ex) { sinCargar.Add((nombreZip, "no se pudo abrir el ZIP: " + ex.Message)); continue; }
                var (n, falla) = await SubirZipAlGestorAsync(files, cufe, zipBytes, ct);
                subidos += n;
                if (falla is not null) { sinCargar.Add((nombreZip, falla)); }
            }
        }

        var detalle = $"Procesados {archivos.Length}: {nuevos} nuevos, {actualizados} actualizados"
            + (fallidos > 0 ? $", {fallidos} con error" : "")
            + (cfg.AutoImportar ? $"; {importados} importados a conciliacion ({periodos.Count} periodo/s)" : "")
            + (cfg.SubirABlob ? $"; {subidos} archivo(s) al blob" : "")
            + (cfg.MoverProcesados ? "; movidos a /procesados" : "") + "."
            + (sinCargar.Count > 0 ? " " + ResumenSinCargar(sinCargar) : "");
        // Con documentos sin cargar el paso queda en AVISO (Ok=false): el flujo sigue (no es el OTP), pero el
        // operador lo ve en el modal en vez de un "ok" enganoso.
        var okPaso = sinCargar.Count == 0;
        await RecordStepRunAsync(fdb, flowId, step.Name, okPaso, nuevos, actualizados, 0, detalle, ct);
        return new StepRunResult(okPaso, false, okPaso ? null : detalle, null, null, nuevos, actualizados, 0, detalle);
    }

    /// <summary>Camino "via agente" de ProcesarArchivos: la carpeta de descargas vive en la maquina del
    /// AGENTE (Windows) y NO en el servidor. Lista y lee los ZIP a traves del sub-agente Archivos
    /// (List + ReadBytes -> base64), parsea DESDE LOS BYTES (DianUblParser.ParseZipBytes), hace el MISMO
    /// upsert por CUFE y sube PDF/XML al blob, todo server-side. "Mover procesados" aqui se traduce a
    /// BORRAR el ZIP de origen tras confirmar (el blob ya conserva PDF+XML); si la raiz no es de escritura
    /// (sin 'rw:') no borra y el dedup por CUFE evita reprocesar en vano. Requiere que el operador habilite
    /// "Archivos" y agregue la carpeta a la allow-list de la colmena.</summary>
    private async Task<StepRunResult> RunProcesarArchivosViaAgenteAsync(
        IApplicationDbContext db, Guid flowId, Guid tenantId, ScrapeStep step, ProcesarArchivosConfig cfg,
        string carpeta, string patron, string nuestroNit, CancellationToken ct, Action<string>? live = null)
    {
        // Resolver el agente asignado al flujo (mismo patron que RunStepNowAsync) y exigir que este en linea.
        var flow = await db.ScrapeFlows.FirstOrDefaultAsync(f => f.Id == flowId, ct);
        if (flow?.ClientId is not Guid clientPk)
        {
            var m = "El flujo no tiene un agente asignado (necesario para leer la carpeta de descargas remota).";
            await RecordStepRunAsync(db, flowId, step.Name, false, 0, 0, 0, m, ct);
            return new StepRunResult(false, false, m, null, null, 0, 0, 0, m);
        }
        var client = await db.DataClients.FirstOrDefaultAsync(c => c.Id == clientPk && c.IsActive, ct);
        if (client is null)
        {
            var m = "El agente asignado no existe o esta inactivo.";
            await RecordStepRunAsync(db, flowId, step.Name, false, 0, 0, 0, m, ct);
            return new StepRunResult(false, false, m, null, null, 0, 0, 0, m);
        }
        if (!registry.IsOnline(client.ClientId))
        {
            var m = "El agente asignado no esta en linea (no se puede leer la carpeta de descargas remota).";
            await RecordStepRunAsync(db, flowId, step.Name, false, 0, 0, 0, m, ct);
            return new StepRunResult(false, true, m, null, null, 0, 0, 0, m);
        }
        var clientId = client.ClientId;
        var tenantStr = tenantId.ToString();

        // Esperar a que las descargas se ASIENTEN: encuesta la carpeta (List remoto) hasta que el conteo de
        // archivos que casan con el patron no cambie durante EsperarAsentarSegundos (y haya >=1), con gracia
        // inicial y tope. Robusto por construccion: mira el filesystem del agente, no la señal del bucle.
        if (cfg.EsperarAsentarSegundos > 0)
        {
            const int intervalo = 5;
            int lastCount = -1, estableSeg = 0, total = 0, fallosList = 0;
            while (total < cfg.MaxEsperaSegundos)
            {
                int n;
                try
                {
                    n = (await AgentListMatchingAsync(clientId, tenantStr, carpeta, patron, ct)).Count;
                    fallosList = 0;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    // Un parpadeo del canal se reintenta al siguiente tick. Pero un error de CONFIGURACION
                    // (carpeta fuera de la allow-list de la colmena, inexistente) o 3 fallos seguidos no se
                    // arreglan esperando: se corta ya con el motivo, en vez de agotar MaxEsperaSegundos (15 min).
                    fallosList++;
                    if (EsErrorPermanenteArchivos(ex.Message) || fallosList >= 3)
                    {
                        var m = $"No se pudo listar la carpeta de descargas en el agente ({carpeta}): {ex.Message}"
                            + (EsErrorPermanenteArchivos(ex.Message)
                                ? ". Agrega esa carpeta en la colmena -> Archivos (con prefijo 'rw:' para que pueda borrar los procesados)."
                                : ".");
                        await RecordStepRunAsync(db, flowId, step.Name, false, 0, 0, 0, m, ct);
                        return new StepRunResult(false, false, m, null, null, 0, 0, 0, m);
                    }
                    n = lastCount;
                }
                if (n == lastCount) { estableSeg += intervalo; } else { estableSeg = 0; lastCount = n; }
                if (n > 0 && estableSeg >= cfg.EsperarAsentarSegundos) { break; }
                if (n == 0 && total >= cfg.GraciaInicialSegundos) { break; }
                await Task.Delay(TimeSpan.FromSeconds(intervalo), ct);
                total += intervalo;
            }
        }

        // DbContext fresco tras la espera (la conexion del scope original puede quedar stale en una espera larga).
        using var dbScope = scopeFactory.CreateScope();
        var fdb = dbScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        List<string> archivos;
        try { archivos = await AgentListMatchingAsync(clientId, tenantStr, carpeta, patron, ct); }
        catch (Exception ex)
        {
            var m = $"No se pudo listar la carpeta del agente ({carpeta}): {ex.Message}"
                + (EsErrorPermanenteArchivos(ex.Message)
                    ? ". Agrega esa carpeta en la colmena -> Archivos (con prefijo 'rw:')."
                    : ".");
            await RecordStepRunAsync(fdb, flowId, step.Name, false, 0, 0, 0, m, ct);
            return new StepRunResult(false, false, m, null, null, 0, 0, 0, m);
        }
        if (archivos.Count == 0)
        {
            var m = "No hay archivos para procesar en la carpeta del agente.";
            await RecordStepRunAsync(fdb, flowId, step.Name, true, 0, 0, 0, m, ct);
            return new StepRunResult(true, false, null, null, null, 0, 0, 0, m);
        }

        var sep = carpeta.Contains('\\') ? '\\' : '/'; // la ruta remota es de la maquina del agente (Windows).
        var procDir = carpeta.TrimEnd(sep) + sep + "procesados";
        int nuevos = 0, actualizados = 0, fallidos = 0;
        var periodos = new HashSet<(int Anio, int Mes)>();
        var okBytes = new Dictionary<string, byte[]>(); // nombre -> bytes del ZIP (para blob + borrado), solo los OK
        var cufePorZip = new List<(string Cufe, string Archivo)>();
        var sinCargar = new List<(string Nombre, string Motivo)>(); // documentos que NO quedaron completos

        var procIdx = 0;
        foreach (var nombre in archivos)
        {
            live?.Invoke($"leyendo del agente {++procIdx}/{archivos.Count}");
            var rutaRemota = carpeta.TrimEnd(sep) + sep + nombre;
            var (zipBytes, motivoLectura) = await LeerZipDelAgenteConReintentoAsync(clientId, tenantStr, rutaRemota, ct);
            if (zipBytes is null)
            {
                fallidos++;
                sinCargar.Add((nombre, "no se pudo leer del agente: " + (motivoLectura ?? "sin detalle")));
                continue;
            }

            DianDoc? doc;
            string? motivoParse = null;
            try { doc = DianUblParser.ParseZipBytes(zipBytes, nuestroNit); }
            catch (Exception ex) { doc = null; motivoParse = ex.Message; }
            if (doc is null || string.IsNullOrWhiteSpace(doc.Cufe))
            {
                fallidos++;
                sinCargar.Add((nombre,
                    "no se pudo leer como factura DIAN" + (motivoParse is null ? " (sin CUFE)" : ": " + motivoParse)));
                continue;
            }

            var existente = await fdb.ConciliacionDianBotDummies.FirstOrDefaultAsync(b => b.Cufe == doc.Cufe, ct);
            if (existente is null)
            {
                fdb.ConciliacionDianBotDummies.Add(new ConciliacionDianBotDummy
                {
                    TenantId = tenantId,
                    Cufe = doc.Cufe,
                    TipoDoc = doc.TipoDoc,
                    FechaDoc = doc.Fecha,
                    NombreEmisor = doc.NombreEmisor,
                    NitEmisor = doc.NitEmisor,
                    PrefijoFolio = doc.NumFactura,
                    IdCompra = "",
                    SubtotalBruto = doc.SubtotalBruto,
                    DescuentoComercial = doc.Descuento,
                    Subtotal = doc.SubtotalNeto,
                    Iva = doc.Iva,
                    TotalAntesRet = doc.TotalAntesRet,
                    RetencionFuente = doc.Retefuente,
                    RetencionIca = doc.ReteIca,
                    Total = doc.Total,
                    TipoPago = doc.TipoPago,
                    ProveedorTecnologico = "",
                });
                nuevos++;
            }
            else
            {
                existente.TipoDoc = doc.TipoDoc; existente.FechaDoc = doc.Fecha;
                existente.NombreEmisor = doc.NombreEmisor; existente.NitEmisor = doc.NitEmisor;
                existente.PrefijoFolio = doc.NumFactura; existente.SubtotalBruto = doc.SubtotalBruto;
                existente.DescuentoComercial = doc.Descuento; existente.Subtotal = doc.SubtotalNeto;
                existente.Iva = doc.Iva; existente.TotalAntesRet = doc.TotalAntesRet;
                existente.RetencionFuente = doc.Retefuente; existente.RetencionIca = doc.ReteIca;
                existente.Total = doc.Total; existente.TipoPago = doc.TipoPago;
                actualizados++;
            }
            periodos.Add((doc.Fecha.Year, doc.Fecha.Month));
            okBytes[nombre] = zipBytes;
            cufePorZip.Add((doc.Cufe, nombre));
        }
        await fdb.SaveChangesAsync(ct);

        // Sube al gestor unificado (blob) el PDF y el XML de cada ZIP, por CUFE, DESDE LOS BYTES en memoria. Idempotente.
        int subidos = 0;
        if (cfg.SubirABlob && cufePorZip.Count > 0)
        {
            var files = dbScope.ServiceProvider.GetRequiredService<IFileStorageService>();
            foreach (var (cufe, nombre) in cufePorZip)
            {
                if (!okBytes.TryGetValue(nombre, out var bytes)) { continue; }
                var (n, falla) = await SubirZipAlGestorAsync(files, cufe, bytes, ct);
                subidos += n;
                if (falla is not null)
                {
                    sinCargar.Add((nombre, falla));
                    // CLAVE: aqui "mover procesados" BORRA el ZIP de origen. Si sus documentos no quedaron en el
                    // gestor, NO se borra: se conserva para que la proxima corrida reintente la subida (el dedup
                    // por CUFE evita duplicar la fila). Antes se borraba igual y la factura quedaba sin PDF/XML
                    // para siempre.
                    okBytes.Remove(nombre);
                }
            }
        }

        // "Mover procesados" en remoto = BORRAR el ZIP de origen tras confirmar (el blob ya conserva PDF+XML).
        // Si la carpeta no es de escritura (sin 'rw:' en la allow-list) el borrado falla y no pasa nada: el
        // dedup por CUFE evita duplicar en la proxima corrida (solo se re-leeran los bytes en vano).
        int movidos = 0; bool borradoBloqueado = false;
        if (cfg.MoverProcesados && okBytes.Count > 0)
        {
            try
            {
                var acciones = okBytes.Keys
                    .Select(nombre => new FileAction(FileActionKind.Delete, carpeta.TrimEnd(sep) + sep + nombre))
                    .ToList();
                var corr = NewCorr();
                var res = await fileChannel.ExecuteFilesAsync(clientId,
                    new FileRequestMsg(corr, tenantStr, acciones), TimeSpan.FromSeconds(60), ct);
                movidos = res.Results.Count(r => r.Ok);
                if (res.Results.Any(r => !r.Ok)) { borradoBloqueado = true; }
            }
            catch { borradoBloqueado = true; }
        }

        int importados = 0;
        if (cfg.AutoImportar)
        {
            var svc = dbScope.ServiceProvider.GetRequiredService<IConciliacionDianService>();
            foreach (var (anio, mes) in periodos)
            {
                var ccd = await fdb.ConciliacionDianDocumentos.FirstOrDefaultAsync(d => d.Anio == anio && d.Mes == mes, ct);
                Guid? docId = ccd?.Id;
                if (docId is null)
                {
                    var (nuevo, _) = await svc.CrearDocumentoAsync(anio, mes, ct);
                    docId = nuevo?.Id;
                }
                if (docId is Guid id) { importados += await svc.ImportarDesdeBotAsync(id, ct); }
            }
        }

        var detalle = $"(via agente) Procesados {archivos.Count}: {nuevos} nuevos, {actualizados} actualizados"
            + (fallidos > 0 ? $", {fallidos} con error" : "")
            + (cfg.AutoImportar ? $"; {importados} importados a conciliacion ({periodos.Count} periodo/s)" : "")
            + (cfg.SubirABlob ? $"; {subidos} archivo(s) al blob" : "")
            + (cfg.MoverProcesados ? (borradoBloqueado
                ? "; no se borraron los ZIP de origen (carpeta sin permiso 'rw:'; el dedup por CUFE evita duplicar)"
                : $"; {movidos} ZIP borrados del origen") : "") + "."
            + (sinCargar.Count > 0
                ? " " + ResumenSinCargar(sinCargar) + " Sus ZIP se conservan en la carpeta del agente para reintentar."
                : "");
        // Con documentos sin cargar el paso queda en AVISO (Ok=false): el flujo sigue (no es el OTP), pero el
        // operador lo ve en el modal en vez de un "ok" enganoso.
        var okPaso = sinCargar.Count == 0;
        await RecordStepRunAsync(fdb, flowId, step.Name, okPaso, nuevos, actualizados, 0, detalle, ct);
        return new StepRunResult(okPaso, false, okPaso ? null : detalle, null, null, nuevos, actualizados, 0, detalle);
    }

    /// <summary>Lista la carpeta remota via sub-agente Archivos y devuelve los NOMBRES de archivo (no dirs)
    /// que casan con el patron glob (p.ej. "*.zip"). Lanza si el agente reporta error (p.ej. Archivos no
    /// habilitado o ruta fuera de la allow-list).</summary>
    private async Task<List<string>> AgentListMatchingAsync(
        string clientId, string tenantStr, string carpeta, string patron, CancellationToken ct)
    {
        var corr = NewCorr();
        var res = await fileChannel.ExecuteFilesAsync(clientId,
            new FileRequestMsg(corr, tenantStr, new[] { new FileAction(FileActionKind.List, carpeta) }),
            TimeSpan.FromSeconds(30), ct);
        var r0 = res.Results.Count > 0 ? res.Results[0] : null;
        if (r0 is null || !r0.Ok)
        {
            throw new InvalidOperationException(r0?.Error ?? res.Error ?? "List sin resultado.");
        }
        var regex = GlobToRegex(patron);
        return (r0.Entries ?? Array.Empty<FileEntry>())
            .Where(e => !e.IsDirectory && regex.IsMatch(e.Name))
            .Select(e => e.Name)
            .ToList();
    }

    /// <summary>Error del sub-agente Archivos que NO se arregla reintentando: carpeta fuera de la allow-list
    /// de la colmena, ruta inexistente o acceso denegado. Ante esto se corta con el motivo en vez de esperar.</summary>
    private static bool EsErrorPermanenteArchivos(string? msg) =>
        !string.IsNullOrEmpty(msg)
        && (msg.Contains("allow-list", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("no existe", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("denegad", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("denied", StringComparison.OrdinalIgnoreCase));

    /// <summary>Lee un ZIP de la maquina del agente con hasta 3 intentos (un ZIP recien descargado puede estar
    /// bloqueado por el navegador o el canal tener un parpadeo). Devuelve los bytes o el MOTIVO del ultimo
    /// fallo, para reportar por documento en vez de contarlo como "con error" sin decir cual ni por que.</summary>
    private async Task<(byte[]? Bytes, string? Motivo)> LeerZipDelAgenteConReintentoAsync(
        string clientId, string tenantStr, string rutaRemota, CancellationToken ct)
    {
        string? motivo = null;
        for (var intento = 1; intento <= 3; intento++)
        {
            try
            {
                var res = await fileChannel.ExecuteFilesAsync(clientId,
                    new FileRequestMsg(NewCorr(), tenantStr, new[] { new FileAction(FileActionKind.ReadBytes, rutaRemota) }),
                    TimeSpan.FromSeconds(90), ct);
                var r0 = res.Results.Count > 0 ? res.Results[0] : null;
                if (r0 is not null && r0.Ok && !string.IsNullOrEmpty(r0.Value))
                {
                    try { return (Convert.FromBase64String(r0.Value), null); }
                    catch { motivo = "el agente devolvio datos corruptos"; }
                }
                else
                {
                    motivo = r0?.Error ?? res.Error ?? "sin respuesta del agente";
                }
                // Tope de tamano (5 MB) o carpeta no permitida: reintentar no cambia nada.
                if (EsErrorPermanenteArchivos(motivo) || motivo?.Contains(" MB", StringComparison.OrdinalIgnoreCase) == true) { break; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { motivo = ex.Message; }
            if (intento < 3) { await Task.Delay(TimeSpan.FromSeconds(2 * intento), ct); }
        }
        return (null, motivo);
    }

    /// <summary>Sube al gestor (blob) el PDF y el XML de un ZIP DIAN por CUFE, desde los bytes. Idempotente
    /// (no re-sube lo ya registrado). Devuelve cuantos subio y, si algo quedo SIN cargar, el motivo (antes
    /// se tragaba la excepcion y la factura quedaba sin documentos sin que nadie se enterara).</summary>
    private static async Task<(int Subidos, string? Falla)> SubirZipAlGestorAsync(
        IFileStorageService files, string cufe, byte[] zipBytes, CancellationToken ct)
    {
        var baseName = SafeName(cufe);
        var faltaPdf = await files.FindAsync("ConciliacionDian", cufe, "DIAN_PDF", baseName + "_dian.pdf", ct) is null;
        var faltaXml = await files.FindAsync("ConciliacionDian", cufe, "DIAN_XML", baseName + "_dian.xml", ct) is null;
        if (!faltaPdf && !faltaXml) { return (0, null); }
        var subidos = 0;
        var faltas = new List<string>();
        try
        {
            using var ms = new MemoryStream(zipBytes, writable: false);
            using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            var pdfEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            var xmlEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            if (faltaPdf)
            {
                if (pdfEntry is null) { faltas.Add("el ZIP no trae PDF"); }
                else
                {
                    await files.SaveAsync(new FileStorageSaveRequest("ConciliacionDian", cufe, "DIAN_PDF",
                        baseName + "_dian.pdf", "application/pdf", ReadEntry(pdfEntry)), ct);
                    subidos++;
                }
            }
            if (faltaXml)
            {
                if (xmlEntry is null) { faltas.Add("el ZIP no trae XML"); }
                else
                {
                    await files.SaveAsync(new FileStorageSaveRequest("ConciliacionDian", cufe, "DIAN_XML",
                        baseName + "_dian.xml", "text/xml", ReadEntry(xmlEntry)), ct);
                    subidos++;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { faltas.Add("no se pudo subir al gestor: " + ex.Message); }
        return (subidos, faltas.Count == 0 ? null : string.Join(", ", faltas));
    }

    /// <summary>Resumen legible de los documentos que quedaron SIN cargar (max 5 con nombre + motivo).</summary>
    private static string ResumenSinCargar(List<(string Nombre, string Motivo)> l)
    {
        static string Corto(string n) => n.Length > 20 ? n[..12] + "..." + n[^8..] : n;
        return $"ATENCION: {l.Count} documento(s) SIN CARGAR: "
            + string.Join("; ", l.Take(5).Select(x => $"{Corto(x.Nombre)} ({x.Motivo})"))
            + (l.Count > 5 ? $"; y {l.Count - 5} mas" : "") + ".";
    }

    /// <summary>Traduce un patron glob simple ("*.zip", "FA*.zip") a una Regex anclada e insensible a mayusculas.</summary>
    private static System.Text.RegularExpressions.Regex GlobToRegex(string patron)
    {
        var escaped = System.Text.RegularExpressions.Regex.Escape(patron).Replace("\\*", ".*").Replace("\\?", ".");
        return new System.Text.RegularExpressions.Regex("^" + escaped + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private sealed record IngestaNewtonConfig(
        string? UrlVar = "NEWTON_URL", string? TokenVar = "NEWTON_TOKEN",
        string? CarpetaVar = "RutaDescargaNewton", string? Carpeta = null,
        int DiasAtras = 45, bool BajarXml = true, bool BajarPdf = true,
        int Max = 0, bool SoloNuevos = true,
        // Paso 1 del aterrizaje: puebla la fuente NEWTON (ConciliacionDianNewtonDummy) por CUFE. El CruzarAsync
        // enciende PlataformaProveedor para los CUFE con representacion grafica (PDF).
        bool MarcarPlataforma = true,
        // Paso 2 del aterrizaje: refleja los eventos RADIAN que NEWTON ya trae en el listado (events[]) sobre el
        // renglon de la conciliacion por CUFE (Ev.30/32/33/31/34). Solo marca SI, nunca borra.
        bool ReflejarEventos = true,
        // Sube los {CUFE}.xml / {CUFE}_newton.pdf al gestor unificado de archivos (Azure Blob) por CUFE, para
        // poder verlos desde el modulo. Idempotente (no re-sube lo ya registrado).
        bool SubirABlob = true);

    /// <summary>Ingesta NEWTON (Fase 1: DESCARGA). Autentica con Auth-Token (variable secreta del flujo), lista
    /// /documentos-electronicos?include=events y baja cada {CUFE}.xml y {CUFE}_newton.pdf a una carpeta PROPIA
    /// (CarpetaVar, por defecto RutaDescargaNewton; si no, una subcarpeta "Newton" de RutaDescargaBot). No aterriza
    /// en el modulo (de eso se encarga ProcesarArchivos / una fase posterior).</summary>
    private async Task<StepRunResult> RunIngestaNewtonStepAsync(
        IApplicationDbContext db, Guid flowId, Guid tenantId, ScrapeStep step,
        IReadOnlyDictionary<string, string> vars, CancellationToken ct, Action<string>? live = null)
    {
        IngestaNewtonConfig cfg = new();
        if (!string.IsNullOrWhiteSpace(step.MappingJson))
        {
            try
            {
                cfg = JsonSerializer.Deserialize<IngestaNewtonConfig>(step.MappingJson!,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? cfg;
            }
            catch { /* json viejo/invalido: defaults */ }
        }

        async Task FailLogAsync(string msg) => await RecordStepRunAsync(db, flowId, step.Name, false, 0, 0, 0, msg, ct);

        // Credenciales desde las variables del flujo (la secreta ya viene descifrada en 'vars').
        var urlBase = (vars.TryGetValue(cfg.UrlVar ?? "NEWTON_URL", out var u) ? u : "").Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(urlBase)) { urlBase = "https://facturacion.eycproveedores.com/api"; }
        var token = vars.TryGetValue(cfg.TokenVar ?? "NEWTON_TOKEN", out var t) ? t.Trim() : "";
        if (string.IsNullOrWhiteSpace(token))
        {
            var m = $"Falta el Auth-Token: define la variable secreta '{cfg.TokenVar ?? "NEWTON_TOKEN"}' en el flujo.";
            await FailLogAsync(m);
            return new StepRunResult(false, false, m, null, null, 0, 0, 0, m);
        }

        // Carpeta PROPIA de NEWTON: CarpetaVar -> subcarpeta "Newton" de RutaDescargaBot -> error.
        var carpeta = !string.IsNullOrWhiteSpace(cfg.Carpeta) ? ResolveVars(cfg.Carpeta!, vars)
            : (!string.IsNullOrWhiteSpace(cfg.CarpetaVar) && vars.TryGetValue(cfg.CarpetaVar!, out var rn) && !string.IsNullOrWhiteSpace(rn) ? rn.Trim()
            : (vars.TryGetValue("RutaDescargaBot", out var rb) && !string.IsNullOrWhiteSpace(rb) ? Path.Combine(rb.Trim(), "Newton") : null));
        if (string.IsNullOrWhiteSpace(carpeta))
        {
            var m = $"No hay carpeta destino: define la variable '{cfg.CarpetaVar ?? "RutaDescargaNewton"}' (o RutaDescargaBot).";
            await FailLogAsync(m);
            return new StepRunResult(false, false, m, null, null, 0, 0, 0, m);
        }
        try { Directory.CreateDirectory(carpeta); } catch { /* best-effort */ }

        var nuestroNit = vars.TryGetValue("NIT_EMPRESA", out var ne) ? ne.Trim() : "";

        async Task<HttpResponseMessage> GetAsync(string url)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Auth-Token", token);
            return await _http.SendAsync(req, ct);
        }

        // 1) Validar el token (GET /login).
        try
        {
            using var loginResp = await GetAsync(urlBase + "/login");
            if (!loginResp.IsSuccessStatusCode)
            {
                var body = await loginResp.Content.ReadAsStringAsync(ct);
                var m = $"Login NEWTON fallo ({(int)loginResp.StatusCode}): {Trunc(body, 200)}";
                await FailLogAsync(m);
                return new StepRunResult(false, false, m, null, null, 0, 0, 0, m);
            }
        }
        catch (Exception ex)
        {
            var m = $"No se pudo conectar a NEWTON: {ex.Message}";
            await FailLogAsync(m);
            return new StepRunResult(false, false, m, null, null, 0, 0, 0, m);
        }

        // 2) Listar documentos recibidos (ultimos DiasAtras).
        var desde = DateTime.UtcNow.AddDays(-Math.Abs(cfg.DiasAtras)).ToString("yyyy-MM-dd");
        var listUrl = $"{urlBase}/documentos-electronicos?include=events&order=-created_at&from={desde}";
        JsonElement docsRoot;
        try
        {
            using var listResp = await GetAsync(listUrl);
            var body = await listResp.Content.ReadAsStringAsync(ct);
            if (!listResp.IsSuccessStatusCode)
            {
                var m = $"Listar NEWTON fallo ({(int)listResp.StatusCode}): {Trunc(body, 200)}";
                await FailLogAsync(m);
                return new StepRunResult(false, false, m, null, null, 0, 0, 0, m);
            }
            using var jd = JsonDocument.Parse(body);
            // NEWTON puede responder un arreglo [ ... ] o un objeto { "data": [ ... ] }.
            docsRoot = jd.RootElement.ValueKind == JsonValueKind.Array ? jd.RootElement.Clone()
                : (jd.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array ? d.Clone() : default);
        }
        catch (Exception ex)
        {
            var m = $"No se pudo leer el listado de NEWTON: {ex.Message}";
            await FailLogAsync(m);
            return new StepRunResult(false, false, m, null, null, 0, 0, 0, m);
        }

        if (docsRoot.ValueKind != JsonValueKind.Array)
        {
            var m = "NEWTON no devolvio un listado de documentos.";
            await RecordStepRunAsync(db, flowId, step.Name, true, 0, 0, 0, m, ct);
            return new StepRunResult(true, false, null, null, null, 0, 0, 0, m);
        }

        int total = docsRoot.GetArrayLength();
        int xmlBajados = 0, pdfBajados = 0, pdfOmitidos = 0, errores = 0, i = 0;
        // Aterrizaje: por doc, CUFE + si tiene representacion grafica (PDF en disco) + los datos de negocio
        // parseados del XML (Doc, para enriquecer la fuente NEWTON y materializar fantasmas) + eventos RADIAN.
        var landed = new List<(string Cufe, string EventId, bool PdfOk, DianDoc? Doc, bool E30, bool E31, bool E32, bool E33, bool E34)>();
        // Diagnostico (como eventos_muestra del legacy): cuantos docs traen events[] y una muestra del 1er evento,
        // para poder ajustar el mapeo contra la forma real que devuelva NEWTON.
        int conEventos = 0; string muestraEvento = "";
        // Fallback de CUFE sin re-bajar el XML: usa el mapeo EventId->Cufe que ya quedo en la fuente NEWTON
        // (poblado en corridas previas). Asi una corrida de solo-aterrizaje (BajarXml=false) es rapida.
        var cufePorEvento = new Dictionary<string, string>(StringComparer.Ordinal);
        if (cfg.MarcarPlataforma || cfg.ReflejarEventos || cfg.SubirABlob)
        {
            try
            {
                var pares = await db.ConciliacionDianNewtonDummies.Where(n => n.EventId != "")
                    .Select(n => new { n.EventId, n.Cufe }).ToListAsync(ct);
                foreach (var p in pares) { cufePorEvento[p.EventId] = p.Cufe; }
            }
            catch { /* best-effort */ }
        }
        foreach (var doc in docsRoot.EnumerateArray())
        {
            if (cfg.Max > 0 && i >= cfg.Max) { break; }
            i++;
            live?.Invoke($"descargando {i}/{(cfg.Max > 0 ? Math.Min(total, cfg.Max) : total)}");
            if (doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("events", out var evsDiag)
                && evsDiag.ValueKind == JsonValueKind.Array && evsDiag.GetArrayLength() > 0)
            {
                conEventos++;
                if (muestraEvento.Length == 0) { muestraEvento = Trunc(evsDiag[0].GetRawText(), 300); }
            }
            var eventId = FirstString(doc, "id", "event_id", "eventId");
            if (string.IsNullOrWhiteSpace(eventId)) { errores++; continue; }
            try
            {
                string? xml = null;
                if (cfg.BajarXml)
                {
                    using var xr = await GetAsync($"{urlBase}/documentos-electronicos/{eventId}/xml");
                    if (xr.IsSuccessStatusCode) { xml = await xr.Content.ReadAsStringAsync(ct); }
                }
                // CUFE y datos de negocio: del XML (si se bajo); si no, el CUFE del mapeo EventId->Cufe ya guardado.
                DianDoc? parsed = null;
                if (!string.IsNullOrWhiteSpace(xml))
                {
                    try { parsed = DianUblParser.ParseXml(xml!, nuestroNit); } catch { }
                }
                string cufe = parsed?.Cufe ?? "";
                if (string.IsNullOrWhiteSpace(cufe) && cufePorEvento.TryGetValue(eventId, out var mappedCufe)) { cufe = mappedCufe; }
                var nombre = SafeName(string.IsNullOrWhiteSpace(cufe) ? eventId : cufe);

                if (cfg.BajarXml && !string.IsNullOrWhiteSpace(xml))
                {
                    try { await File.WriteAllTextAsync(Path.Combine(carpeta, nombre + ".xml"), xml, new UTF8Encoding(false), ct); xmlBajados++; }
                    catch { errores++; }
                }

                var pdfPath = Path.Combine(carpeta, nombre + "_newton.pdf");
                if (cfg.BajarPdf)
                {
                    if (cfg.SoloNuevos && File.Exists(pdfPath)) { pdfOmitidos++; }
                    else
                    {
                        using var pr = await GetAsync($"{urlBase}/documentos-electronicos/{eventId}/pdf");
                        if (pr.IsSuccessStatusCode)
                        {
                            var bytes = await pr.Content.ReadAsByteArrayAsync(ct);
                            if (bytes.Length > 0) { await File.WriteAllBytesAsync(pdfPath, bytes, ct); pdfBajados++; }
                            else { errores++; }
                        }
                        else { errores++; }
                    }
                }

                if (!string.IsNullOrWhiteSpace(cufe))
                {
                    var (e30, e31, e32, e33, e34) = MapEventos(doc);
                    landed.Add((cufe, eventId, File.Exists(pdfPath), parsed, e30, e31, e32, e33, e34));
                }
            }
            catch { errores++; }
        }

        // ---- Aterrizaje en el modulo (scope FRESCO: el loop de descarga pudo tardar y dejar stale la conexion
        // del scope original). Paso 1: puebla la fuente NEWTON por CUFE (-> PlataformaProveedor via CruzarAsync).
        // Paso 2: refleja los eventos RADIAN del listado sobre el renglon por CUFE (solo marca SI, nunca borra).
        int plataforma = 0, eventosReng = 0, subidos = 0;
        if ((cfg.MarcarPlataforma || cfg.ReflejarEventos || cfg.SubirABlob) && landed.Count > 0)
        {
            try
            {
                using var dbScope = scopeFactory.CreateScope();
                var fdb = dbScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

                if (cfg.MarcarPlataforma)
                {
                    // Dedup por CUFE; si vino repetido, basta con que alguno tenga PDF, y se toma el primer XML
                    // parseado disponible para enriquecer (proveedor, fecha, totales).
                    var porCufe = landed.GroupBy(x => x.Cufe)
                        .ToDictionary(g => g.Key, g => (
                            EventId: g.First().EventId,
                            PdfOk: g.Any(x => x.PdfOk),
                            Doc: g.Select(x => x.Doc).FirstOrDefault(d => d is not null)));
                    var cufes = porCufe.Keys.ToList();
                    var existentes = await fdb.ConciliacionDianNewtonDummies
                        .Where(n => cufes.Contains(n.Cufe)).ToListAsync(ct);
                    var mapaExist = existentes.ToDictionary(n => n.Cufe, StringComparer.Ordinal);
                    foreach (var (cufe, info) in porCufe)
                    {
                        var guidPdf = info.PdfOk ? SafeName(cufe) + "_newton.pdf" : "";
                        if (mapaExist.TryGetValue(cufe, out var ex))
                        {
                            ex.EventId = info.EventId;
                            if (info.PdfOk) { ex.GuidPdf = guidPdf; } // solo marca; nunca borra un PDF ya registrado
                            EnriquecerNewton(ex, info.Doc);
                        }
                        else
                        {
                            var nd = new ConciliacionDianNewtonDummy
                            {
                                TenantId = tenantId,
                                Cufe = cufe,
                                EventId = info.EventId,
                                GuidPdf = guidPdf,
                            };
                            EnriquecerNewton(nd, info.Doc);
                            fdb.ConciliacionDianNewtonDummies.Add(nd);
                        }
                        plataforma++;
                    }
                }

                if (cfg.ReflejarEventos)
                {
                    // OR de eventos por CUFE (un CUFE puede venir con varios eventos).
                    var evPorCufe = landed.GroupBy(x => x.Cufe).ToDictionary(g => g.Key, g => (
                        E30: g.Any(x => x.E30), E31: g.Any(x => x.E31), E32: g.Any(x => x.E32),
                        E33: g.Any(x => x.E33), E34: g.Any(x => x.E34)));
                    var cufesEv = evPorCufe.Where(kv => kv.Value.E30 || kv.Value.E31 || kv.Value.E32 || kv.Value.E33 || kv.Value.E34)
                        .Select(kv => kv.Key).ToList();
                    if (cufesEv.Count > 0)
                    {
                        // Tenant-scoped por el filtro global (el tenant ambiente sigue activo). Solo renglones que existen.
                        var rengs = await fdb.ConciliacionDianRenglones.Where(r => cufesEv.Contains(r.Cufe)).ToListAsync(ct);
                        // DIAGNOSTICO: por que se reflejan (o no) eventos. Cuantos CUFE con eventos, cuantos cruzan un renglon,
                        // y 3 ejemplos de CUFE-con-evento que NO tienen renglon (para ver si es falta de cruce real).
                        var setReng = rengs.Select(r => r.Cufe).ToHashSet(StringComparer.Ordinal);
                        var sinRenglon = cufesEv.Where(c => !setReng.Contains(c)).Take(3).Select(c => c.Length > 20 ? c[..20] + "…" : c).ToList();
                        log.LogInformation("IngestaNewton reflejo: {ConEv} CUFE con evento(s); {Cruzan} cruzan un renglon; ej. sin renglon: {Ej}",
                            cufesEv.Count, rengs.Count, sinRenglon.Count == 0 ? "(ninguno)" : string.Join(", ", sinRenglon));
                        foreach (var r in rengs)
                        {
                            if (!evPorCufe.TryGetValue(r.Cufe, out var f)) { continue; }
                            var changed = false;
                            if (f.E30 && !r.Evento30) { r.Evento30 = true; changed = true; }
                            if (f.E31 && !r.Evento31) { r.Evento31 = true; changed = true; }
                            if (f.E32 && !r.Evento32) { r.Evento32 = true; changed = true; }
                            if (f.E33 && !r.Evento33) { r.Evento33 = true; changed = true; }
                            if (f.E34 && !r.Evento34) { r.Evento34 = true; changed = true; }
                            if (changed) { eventosReng++; }
                        }
                    }
                }

                await fdb.SaveChangesAsync(ct);

                // Sube los archivos al gestor unificado (blob) por CUFE, para verlos desde el modulo. Idempotente.
                if (cfg.SubirABlob)
                {
                    var files = dbScope.ServiceProvider.GetRequiredService<IFileStorageService>();
                    foreach (var cufe in landed.Select(x => x.Cufe).Distinct())
                    {
                        var baseName = SafeName(cufe);
                        subidos += await SubirSiFaltaAsync(files, "ConciliacionDian", cufe, "NEWTON_XML",
                            Path.Combine(carpeta, baseName + ".xml"), baseName + ".xml", "text/xml", ct);
                        subidos += await SubirSiFaltaAsync(files, "ConciliacionDian", cufe, "NEWTON_PDF",
                            Path.Combine(carpeta, baseName + "_newton.pdf"), baseName + "_newton.pdf", "application/pdf", ct);
                    }
                }
            }
            catch (Exception ex) { log.LogWarning(ex, "IngestaNewton: fallo el aterrizaje en el modulo"); }
        }

        if (cfg.ReflejarEventos) { log.LogInformation("IngestaNewton eventos: {ConEventos} doc(s) con events[]; muestra={Muestra}", conEventos, muestraEvento); }

        var detalle = $"NEWTON: {total} documento(s); {xmlBajados} XML y {pdfBajados} PDF bajados a '{carpeta}'"
            + (pdfOmitidos > 0 ? $", {pdfOmitidos} PDF ya existian" : "")
            + (cfg.MarcarPlataforma ? $"; {plataforma} en plataforma (fuente NEWTON)" : "")
            + (cfg.ReflejarEventos ? $"; {conEventos} doc(s) con eventos en NEWTON -> {eventosReng} renglon(es) reflejados" : "")
            + (cfg.SubirABlob ? $"; {subidos} archivo(s) al blob" : "")
            + (errores > 0 ? $", {errores} con error" : "") + ".";
        await RecordStepRunAsync(db, flowId, step.Name, true, xmlBajados, pdfBajados, 0, detalle, ct);
        return new StepRunResult(true, false, null, null, null, xmlBajados, pdfBajados, 0, detalle);
    }

    /// <summary>Backfill: rellena los datos de negocio (fecha, NIT, emisor, totales) de las filas de la fuente
    /// NEWTON que aun no los tienen, re-parseando el {CUFE}.xml YA GUARDADO en el gestor de archivos (blob),
    /// SIN re-descargar de NEWTON. Habilita que el historico participe de la comparacion DIAN/NEWTON por periodo.
    /// Idempotente: solo toca filas sin NIT emisor (aun no enriquecidas). Devuelve (enriquecidas, sin archivo,
    /// total pendientes).</summary>
    public async Task<(int Enriquecidas, int SinArchivo, int Total)> BackfillNewtonDesdeBlobAsync(Guid tenantId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var files = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        // Filas aun sin enriquecer (proxy: sin NIT emisor). Tenant explicito (no dependemos del filtro ambiente).
        var pendientes = await db.ConciliacionDianNewtonDummies.IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId && n.Cufe != "" && n.NitEmisor == "")
            .ToListAsync(ct);

        int enriquecidas = 0, sinArchivo = 0, hechas = 0;
        foreach (var n in pendientes)
        {
            FileStorageContent? content = null;
            try
            {
                var archivos = await files.ListAsync("ConciliacionDian", n.Cufe, "NEWTON_XML", ct);
                var first = archivos.FirstOrDefault();
                if (first is not null) { content = await files.ReadAsync(first.Id, ct); }
            }
            catch { /* best-effort por fila */ }

            if (content is null) { sinArchivo++; continue; }
            DianDoc? d = null;
            // nuestroNit vacio: en facturas RECIBIDAS el emisor es AccountingSupplierParty, que el parser toma igual.
            try { d = DianUblParser.ParseXml(Encoding.UTF8.GetString(content.Content), ""); } catch { }
            if (d is null || string.IsNullOrWhiteSpace(d.NitEmisor)) { sinArchivo++; continue; }
            EnriquecerNewton(n, d);
            enriquecidas++;
            if (++hechas % 200 == 0) { await db.SaveChangesAsync(ct); }
        }
        if (enriquecidas > 0) { await db.SaveChangesAsync(ct); }
        log.LogInformation("Backfill NEWTON: {Enr} enriquecidas, {Sin} sin archivo, de {Tot} pendientes (tenant {T}).",
            enriquecidas, sinArchivo, pendientes.Count, tenantId);
        return (enriquecidas, sinArchivo, pendientes.Count);
    }

    // Copia los datos de negocio parseados del XML a la fila de la fuente NEWTON (para materializar fantasmas
    // con datos reales y acotar por periodo via FechaDoc). Si no hubo XML (d == null) deja la fila como estaba.
    private static void EnriquecerNewton(ConciliacionDianNewtonDummy n, DianDoc? d)
    {
        if (d is null) { return; }
        n.TipoDoc = d.TipoDoc;
        n.FechaDoc = d.Fecha;
        n.NombreEmisor = d.NombreEmisor;
        n.NitEmisor = d.NitEmisor;
        n.NumeroFactura = d.NumFactura;
        n.SubtotalBruto = d.SubtotalBruto;
        n.DescuentoComercial = d.Descuento;
        n.SubtotalNeto = d.SubtotalNeto;
        n.Iva = d.Iva;
        n.TotalAntesRet = d.TotalAntesRet;
        n.RetencionFuente = d.Retefuente;
        n.RetencionIca = d.ReteIca;
        n.Total = d.Total;
        n.TipoPago = d.TipoPago;
    }

    // Eventos RADIAN que NEWTON ya trae en el listado (?include=events). Calca ColumnaEvento del legacy:
    // identifica por codigo DIAN (030..034) o por nombre, revisando las claves habituales. Si no reconoce, "".
    private static (bool E30, bool E31, bool E32, bool E33, bool E34) MapEventos(JsonElement doc)
    {
        bool e30 = false, e31 = false, e32 = false, e33 = false, e34 = false;
        if (doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("events", out var evs) && evs.ValueKind == JsonValueKind.Array)
        {
            foreach (var ev in evs.EnumerateArray())
            {
                switch (ColumnaEvento(ev))
                {
                    case "30": e30 = true; break;
                    case "31": e31 = true; break;
                    case "32": e32 = true; break;
                    case "33": e33 = true; break;
                    case "34": e34 = true; break;
                }
            }
        }
        return (e30, e31, e32, e33, e34);
    }

    private static string ColumnaEvento(JsonElement ev)
    {
        var sb = new StringBuilder();
        if (ev.ValueKind == JsonValueKind.Object)
        {
            foreach (var k in new[] { "type", "event_type", "eventType", "tipo", "code", "codigo", "name", "nombre", "event" })
            {
                if (ev.TryGetProperty(k, out var v) && v.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                {
                    sb.Append(' ').Append(v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString());
                }
            }
        }
        else { sb.Append(ev.ToString()); }
        var t = sb.ToString().ToUpperInvariant();
        if (t.Trim().Length == 0) { return ""; }
        if (t.Contains("ACUSE") || t.Contains("030")) { return "30"; }
        if (t.Contains("RECLAMO") || t.Contains("031")) { return "31"; }
        if (t.Contains("PRESTACION") || t.Contains("032")) { return "32"; }
        if (t.Contains("ACEPTACION") || t.Contains("033")) { return "33"; }
        if (t.Contains("034")) { return "34"; }
        return "";
    }

    private static string Trunc(string? s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n]);

    private static string SafeName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) { s = s.Replace(c, '_'); }
        return s.Trim();
    }

    /// <summary>Lee los bytes de una entrada de un ZIP.</summary>
    private static byte[] ReadEntry(System.IO.Compression.ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Sube un archivo del disco al gestor unificado (blob) por su clave logica (modulo+ref1+ref2+nombre),
    /// solo si no estaba ya registrado (idempotente). Devuelve 1 si lo subio, 0 si no existia o ya estaba.</summary>
    private static async Task<int> SubirSiFaltaAsync(IFileStorageService files, string module, string ref1,
        string ref2, string path, string fileName, string contentType, CancellationToken ct)
    {
        if (!File.Exists(path)) { return 0; }
        if (await files.FindAsync(module, ref1, ref2, fileName, ct) is not null) { return 0; }
        var bytes = await File.ReadAllBytesAsync(path, ct);
        await files.SaveAsync(new FileStorageSaveRequest(module, ref1, ref2, fileName, contentType, bytes), ct);
        return 1;
    }

    private static string FirstString(JsonElement o, params string[] keys)
    {
        if (o.ValueKind != JsonValueKind.Object) { return ""; }
        foreach (var k in keys)
        {
            if (o.TryGetProperty(k, out var v) && v.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : v.ToString();
            }
        }
        return "";
    }

    /// <summary>Reemplaza @@VAR@@ y {{VAR}} por el valor de la variable (para rutas configurables).</summary>
    private static string ResolveVars(string input, IReadOnlyDictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(input)) { return input; }
        foreach (var (k, v) in vars)
        {
            input = input.Replace("@@" + k + "@@", v).Replace("{{" + k + "}}", v);
        }
        return input;
    }

    public async Task<StepRunResult> RunLoopNowAsync(Guid flowId, Guid loopStepId, Guid tenantId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        using (AmbientTenantContext.Begin(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
            var ingest = scope.ServiceProvider.GetRequiredService<IRowIngestService>();

            var flow = await db.ScrapeFlows.Include(f => f.Steps).Include(f => f.Variables)
                .FirstOrDefaultAsync(f => f.Id == flowId, ct);
            if (flow is null) { return LoopFail("El flujo no existe o no es de este tenant."); }

            var ordered = flow.Steps.OrderBy(s => s.Order).ToList();
            var startIdx = ordered.FindIndex(s => s.Id == loopStepId);
            if (startIdx < 0) { return LoopFail("El paso del bucle no existe en el flujo."); }
            var start = ordered[startIdx];
            if (string.IsNullOrWhiteSpace(start.LoopOverVar))
            {
                return LoopFail("Ese paso no inicia un bucle: marcale 'Recorrer (ForEach) la variable'.");
            }
            // Cuerpo = desde el paso de inicio hasta el primero con IsLoopEnd (inclusive); si ninguno lo tiene,
            // el cuerpo es solo el paso de inicio.
            var endIdx = startIdx;
            for (var i = startIdx; i < ordered.Count; i++) { endIdx = i; if (ordered[i].IsLoopEnd) { break; } }
            var body = ordered.GetRange(startIdx, endIdx - startIdx + 1);

            if (flow.ClientId is not Guid clientPk) { return LoopFail("El flujo no tiene un agente asignado."); }
            var client = await db.DataClients.FirstOrDefaultAsync(c => c.Id == clientPk && c.IsActive, ct);
            if (client is null) { return LoopFail("El agente asignado no existe o esta inactivo."); }
            if (!registry.IsOnline(client.ClientId))
            {
                return new StepRunResult(false, true, "El agente asignado no esta en linea.", null, null, 0, 0, 0, null);
            }

            string? secret = null;
            if (client.ClientSecretEncrypted is not null)
            {
                try { secret = protector.Unprotect(client.ClientSecretEncrypted); } catch { /* ilegible */ }
            }

            var vars = DecryptVariables(flow.Variables, protector);
            if (_stepSessionVars.TryGetValue(flowId, out var sess)) { foreach (var (k, v) in sess) { vars[k] = v; } }
            var ctx = new ScrapeRunContext(vars);

            if (!ctx.TryResolve(start.LoopOverVar!, out var arrJson) || string.IsNullOrWhiteSpace(arrJson))
            {
                return LoopFail($"No hay datos en '{start.LoopOverVar}'. Corre antes el paso que lo captura (OutputVar).");
            }

            var sessionKey = StepSessionKeyFor(flowId);
            var paginate = !string.IsNullOrWhiteSpace(start.PageCountVar);
            // Pasos de avance de pagina (Ola 3): los IsPageNext que pertenecen a ESTE bucle, es decir los que
            // caen despues de su inicio y antes del siguiente inicio de bucle (otro LoopOverVar). Asi un flujo
            // con dos bucles paginados (p.ej. Pendientes e Historico) no mezcla sus avances de pagina.
            var nextLoopStartOrder = ordered
                .Where(s => !string.IsNullOrWhiteSpace(s.LoopOverVar) && s.Order > start.Order)
                .Select(s => (int?)s.Order).Min();
            var pageSteps = ordered.Where(s => s.IsPageNext && s.Order > start.Order
                && (nextLoopStartOrder is null || s.Order < nextLoopStartOrder)).ToList(); // avance+recarga entre paginas
            int ins = 0, upd = 0, del = 0, rowsOk = 0, pagesDone = 0, tareasCreadas = 0;
            var actividadNotas = new List<string>();
            string? lastErr = null, lastShot = null;
            var started = DateTimeOffset.UtcNow;

            // Ejecuta UN paso (del cuerpo o de avance) contra el contexto: compila, despacha (sesion viva),
            // captura su OutputVar e ingiere si tiene IngestPath. Devuelve false y fija lastErr si falla.
            async Task<bool> ExecStepAsync(ScrapeStep step)
            {
                if (step.Kind is ScrapeStepKind.Ai or ScrapeStepKind.LeerCorreoOtp) { return true; } // no en el bucle

                // ENSAMBLADO -> Regla (Ola 4): no va al navegador; invoca la regla con el contexto.
                if (step.RuleId is not null || step.Kind == ScrapeStepKind.Ensamblado)
                {
                    var rules = scope.ServiceProvider.GetRequiredService<IRulesEngine>();
                    var (rok, rdetail, _) = await InvokeRuleAsync(rules, step, ctx, ct);
                    if (!rok) { lastErr = rdetail; return false; }
                    return true;
                }

                // Crear actividad (server-side): una tarea por fila del bucle, sin duplicar. Un fallo de UNA fila
                // (p.ej. concepto mal configurado) corta el bucle como cualquier otro paso, para que se vea.
                if (step.Kind == ScrapeStepKind.CrearActividad)
                {
                    var ca = await ScrapeCrearActividad.RunAsync(scope.ServiceProvider, db, tenantId, step, ctx,
                        $"Dron {flow.Name}", ct);
                    foreach (var (k, v) in ScrapeCrearActividad.OutputVars(step, ca)) { ctx.Set(k, v); }
                    if (!ca.Ok) { lastErr = ca.Detail; return false; }
                    if (ca.Created) { tareasCreadas++; }
                    actividadNotas.Add(ca.Detail);
                    return true;
                }

                // Notificar (server-side, best-effort): un fallo de ENVIO no corta el bucle; una config invalida si.
                if (step.Kind == ScrapeStepKind.Notificar)
                {
                    var nt = await ScrapeNotificar.RunAsync(scope.ServiceProvider, db, tenantId, step, ctx, $"Dron {flow.Name}", ct);
                    if (!nt.Ok) { lastErr = nt.Detail; return false; }
                    actividadNotas.Add(nt.Detail);
                    return true;
                }

                // El correlationId debe ser EL MISMO al compilar (que FIRMA el JS ligado a ese corr) y al
                // despachar: el agente verifica la firma contra el corr del request. Usar dos distintos =
                // "Firma de JS invalida".
                var corr = NewCorr();
                var prepared = SubstitutedClone(step, ctx);
                CompiledFlow compiled;
                try { compiled = ScrapeFlowCompiler.CompileSteps(new[] { prepared }, flow.ContainerId, EmptyVars, corr, secret); }
                catch (ScrapeCompileException ex) { lastErr = ex.Message; return false; }
                if (compiled.Actions.Count == 0) { return true; }

                var actions = compiled.Actions.Append(new BrowserAction(BrowserActionKind.Screenshot, Screenshot: true)).ToList();
                var timeout = TimeSpan.FromSeconds(60 + actions.Sum(a => (a.WaitMs ?? 0) / 1000.0));
                BrowserResultMsg result;
                try
                {
                    var req = new BrowserRequestMsg(corr, tenantId.ToString(), actions, SessionKey: sessionKey, KeepAlive: true, DownloadFolder: DownloadFolderFrom(vars));
                    result = await channel.ExecuteAsync(client.ClientId, req, timeout, ct);
                }
                catch (Exception ex) { lastErr = ex.Message; return false; }

                lastShot = result.Results.LastOrDefault(r => !string.IsNullOrEmpty(r.ScreenshotBase64))?.ScreenshotBase64 ?? lastShot;
                if (!result.Ok) { lastErr = FirstError(result); return false; }

                if (!string.IsNullOrWhiteSpace(step.OutputVar))
                {
                    var val = result.Results.Where(r => r.Kind != BrowserActionKind.Screenshot && !string.IsNullOrEmpty(r.Value))
                        .Select(r => r.Value).FirstOrDefault();
                    ctx.Set(step.OutputVar, UnwrapEvalValue(val));
                }
                if (!string.IsNullOrWhiteSpace(step.IngestPath) && ctx.TryResolve(step.IngestPath!, out var ingJson))
                {
                    var container = step.TargetContainerId ?? flow.ContainerId;
                    if (container is Guid cid)
                    {
                        var rows2 = ScrapeRowIngest.ParseRows(ingJson);
                        if (rows2.Count > 0)
                        {
                            var (i2, u2, d2) = await ScrapeRowIngest.IngestAsync(ingest, db, cid, tenantId, step.MappingJson, rows2, ct, step.IngestKeyColumn);
                            ins += i2; upd += u2; del += d2;
                        }
                    }
                }
                return true;
            }

            // Las filas del array actual (p.ej. LISTADO.compras) tal cual esta el contexto en esta pagina.
            List<string> CurrentRows()
            {
                if (!ctx.TryResolve(start.LoopOverVar!, out var aj) || string.IsNullOrWhiteSpace(aj)) { return new(); }
                try
                {
                    using var d = JsonDocument.Parse(aj);
                    return d.RootElement.ValueKind == JsonValueKind.Array
                        ? d.RootElement.EnumerateArray().Select(e => e.GetRawText()).ToList() : new();
                }
                catch { return new(); }
            }

            const int MaxPages = 500; // tope de seguridad por si el total viene mal
            // Tope de filas (variable del flujo TopeFilasBucle, ADR-0168): para simular o probar con pocos
            // registros sin consumir todo el listado. Vacio/0 = sin tope.
            var topeFilas = LoopRowCap(vars);
            var topeAlcanzado = false;
            var page = 1;
            while (true)
            {
                var rows = CurrentRows();
                for (var ri = 0; ri < rows.Count; ri++)
                {
                    if (topeFilas > 0 && rowsOk >= topeFilas) { topeAlcanzado = true; break; }
                    ctx.Set("ROW", rows[ri]);
                    ctx.Set("ROW_INDEX", ri.ToString());
                    var rowOk = true;
                    foreach (var step in body) { if (!await ExecStepAsync(step)) { rowOk = false; break; } }
                    if (rowOk) { rowsOk++; } else { break; }
                }
                pagesDone++;
                if (lastErr is not null || !paginate || topeAlcanzado || (topeFilas > 0 && rowsOk >= topeFilas)) { break; }

                var total = 1;
                if (ctx.TryResolve(start.PageCountVar!, out var tp) && int.TryParse((tp ?? "").Trim(), out var tpn)) { total = tpn; }
                if (page >= total || page >= MaxPages) { break; }

                // Avanzar de pagina: fija PAG y corre los pasos marcados IsPageNext (postback + re-lectura del
                // listado, que al tener OutputVar refresca LISTADO en el contexto para la proxima vuelta).
                page++;
                ctx.Set("PAG", page.ToString());
                var advOk = true;
                foreach (var ps in pageSteps) { if (!await ExecStepAsync(ps)) { advOk = false; break; } }
                if (!advOk) { break; }
            }

            var okAll = lastErr is null;
            var detail = $"{rowsOk} filas consumidas en {pagesDone} pagina(s), {ins} ingeridas"
                + (topeFilas > 0 ? $" (tope {topeFilas} filas)" : "")
                + (actividadNotas.Count > 0 ? $", {tareasCreadas} actividad(es) creada(s)" : "")
                + (lastErr is null ? "" : $"; ultimo error: {lastErr}");
            if (actividadNotas.Count > 0)
            {
                log.LogInformation("[NAV-LOOP] {Flow} crear actividad: {Notas}", flow.Name, string.Join(" | ", actividadNotas));
            }
            await activity.RecordAsync(new AgentActivityEntry(
                tenantId, client.ClientId, null, AgentActivityKind.Browser, NewCorr(),
                $"Bucle: {flow.Name} / {start.Name}", okAll, started, DateTimeOffset.UtcNow, detail));
            await RecordStepRunAsync(db, flowId, $"Bucle {start.Name}", okAll, ins, upd, del, detail, ct);
            return new StepRunResult(okAll, false, lastErr, lastShot, detail, ins, upd, del, detail);
        }

        static StepRunResult LoopFail(string error) => new(false, false, error, null, null, 0, 0, 0, error);
    }

    public async Task<StepRunResult> CloseStepSessionAsync(Guid flowId, Guid tenantId, CancellationToken ct = default)
    {
        _stepSessionVars.TryRemove(flowId, out _);
        using var scope = scopeFactory.CreateScope();
        using (AmbientTenantContext.Begin(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var flow = await db.ScrapeFlows.FirstOrDefaultAsync(f => f.Id == flowId, ct);
            if (flow?.ClientId is not Guid clientPk) { return new StepRunResult(true, false, null, null, null, 0, 0, 0, "Sin agente."); }
            var client = await db.DataClients.FirstOrDefaultAsync(c => c.Id == clientPk && c.IsActive, ct);
            if (client is null || !registry.IsOnline(client.ClientId))
            {
                return new StepRunResult(true, false, null, null, null, 0, 0, 0, "Sesion local limpiada (agente offline).");
            }
            try
            {
                var corr = NewCorr();
                var req = new BrowserRequestMsg(corr, tenantId.ToString(),
                    new[] { new BrowserAction(BrowserActionKind.CloseSession) }, SessionKey: StepSessionKeyFor(flowId), KeepAlive: false);
                await channel.ExecuteAsync(client.ClientId, req, TimeSpan.FromSeconds(20), ct);
            }
            catch (Exception ex) { log.LogWarning(ex, "[NAV-STEP] no se pudo cerrar la sesion viva del flujo {Flow}", flowId); }
            return new StepRunResult(true, false, null, null, null, 0, 0, 0, "Navegador cerrado.");
        }
    }

    /// <summary>Registra UNA corrida de un paso manual en la bitacora del flujo (ScrapeFlowRun). A diferencia
    /// del cierre de una corrida completa, un fallo aqui NO marca el flujo "con errores" (es depuracion): solo
    /// deja la traza y actualiza el "ultima corrida".</summary>
    private async Task RecordStepRunAsync(IApplicationDbContext db, Guid flowId, string stepName, bool ok,
        int inserted, int updated, int deleted, string? detail, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var text = $"Paso: {stepName}" + (string.IsNullOrWhiteSpace(detail) ? "" : $" - {detail}");
        db.ScrapeFlowRuns.Add(new ScrapeFlowRun
        {
            FlowId = flowId,
            FiredAt = now,
            FinishedAt = now,
            Trigger = ImportRunTrigger.Manual,
            Result = ok ? ImportRunResult.Ok : ImportRunResult.Error,
            CorrelationId = NewCorr(),
            StepCount = 1,
            Inserted = inserted,
            Updated = updated,
            Deleted = deleted,
            Detail = text.Length <= 600 ? text : text[..597] + "...",
        });
        var flow = await db.ScrapeFlows.FirstOrDefaultAsync(f => f.Id == flowId, ct);
        if (flow is not null)
        {
            flow.LastRunAt = now;
            // last_result_summary es varchar(400) (detail es 600): recortar a 400. Antes se recortaba a 600 y
            // un detalle de 401-600 caracteres tumbaba el SaveChanges y el paso quedaba sin registro.
            flow.LastResultSummary = text.Length <= 400 ? text : text[..397] + "...";
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Config del paso "Leer token de correo", serializada en ScrapeStep.MappingJson (misma forma
    /// que la que arma la UI de Extraccion de datos).</summary>
    private sealed record OtpStepConfig(Guid? MailboxId, string? From, string? Subject, string? Regex, string? Variable, int TimeoutSeconds, bool UrlDecode = false);

    private static string Shorten(string? s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");

    private static readonly Dictionary<string, string> EmptyVars = new(StringComparer.Ordinal);

    /// <summary>Clon DESLIGADO del paso con Url/Script/Selector ya sustituidos contra el contexto (para no
    /// mutar la entidad rastreada ni persistir el JS sustituido). Copia solo lo que el compilador lee.</summary>
    private static ScrapeStep SubstitutedClone(ScrapeStep s, ScrapeRunContext ctx) => new()
    {
        Order = s.Order,
        Kind = s.Kind,
        Name = s.Name,
        WaitMs = s.WaitMs,
        Url = ctx.Substitute(s.Url),
        // El JS va escapado: cada valor resuelto cae dentro de un literal de cadena del script.
        Script = ctx.SubstituteScript(s.Script),
        Selector = ctx.Substitute(s.Selector),
        MappingJson = s.MappingJson,
        TargetContainerId = s.TargetContainerId,
        WarningLabel = s.WarningLabel,
        WarningAction = s.WarningAction,
    };

    /// <summary>WebView2 ExecuteScriptAsync devuelve el resultado JSON-encoded. Si el paso devolvio una CADENA
    /// (el caso tipico: <c>JSON.stringify({...})</c>), llega doble-codificada ("\"{...}\""): se desenvuelve un
    /// nivel para guardar el JSON limpio navegable. Si no era cadena JSON, se deja tal cual.</summary>
    private static string? UnwrapEvalValue(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) { return raw; }
        var t = raw.TrimStart();
        if (t.Length > 0 && t[0] == '"')
        {
            try { return JsonSerializer.Deserialize<string>(raw); }
            catch { /* no era cadena JSON literal */ }
        }
        return raw;
    }

    // ---- ENSAMBLADO -> Regla (motor del dron, Ola 4) ----

    /// <summary>Invoca la Regla del paso (si tiene RuleId) pasandole como FormData el JSON de RuleInputVar
    /// aplanado. Un paso ENSAMBLADO sin regla asignada es un no-op (no rompe el bucle).</summary>
    private static async Task<(bool Ok, string Detail, int Records)> InvokeRuleAsync(
        IRulesEngine rules, ScrapeStep step, ScrapeRunContext ctx, CancellationToken ct)
    {
        if (step.RuleId is not Guid rid) { return (true, "ENSAMBLADO sin regla asignada (omitido).", 0); }
        var inputPath = string.IsNullOrWhiteSpace(step.RuleInputVar) ? "DETALLE" : step.RuleInputVar!;
        var formData = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (ctx.TryResolve(inputPath, out var json) && !string.IsNullOrWhiteSpace(json))
        {
            FlattenJsonObject(json, formData);
        }
        var res = await rules.ExecuteRuleAsync(rid, new RuleInvocation(RuleTriggerKind.Manual, formData), ct);
        if (!res.IsOk || res.Value is null)
        {
            return (false, res.Error ?? "La regla no se pudo ejecutar.", 0);
        }
        var o = res.Value;
        var ok = o.Status == RuleExecutionStatus.Success;
        var detail = $"Regla {o.RuleName}: {o.Status}"
            + (o.RecordsAffected > 0 ? $" ({o.RecordsAffected} reg.)" : "")
            + (ok ? "" : $" - {o.ErrorMessage ?? o.Message}");
        return (ok, detail, o.RecordsAffected);
    }

    /// <summary>Aplana un objeto JSON a FormData (clave->texto). Escalares como texto; objetos/arreglos como
    /// su JSON crudo. Si no es un objeto, lo deja en la clave "_raw".</summary>
    private static void FlattenJsonObject(string json, Dictionary<string, string?> into)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { into["_raw"] = json; return; }
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                into[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Null => null,
                    JsonValueKind.Object or JsonValueKind.Array => p.Value.GetRawText(),
                    _ => p.Value.ToString()
                };
            }
        }
        catch { into["_raw"] = json; }
    }

    /// <summary>Ejecuta el flujo paso a paso, en su propio scope. Deterministas por tramos (canal),
    /// pasos de IA por el orquestador. Cierra la corrida al terminar, pase lo que pase.</summary>
    private async Task ExecuteFlowAsync(Guid flowId, Guid tenantId, string runCorr, string clientId,
        string? secret, IReadOnlyDictionary<string, string> variables)
    {
        TrackRun(runCorr);
        try { await ExecuteFlowTrackedAsync(flowId, tenantId, runCorr, clientId, secret, variables); }
        finally { UntrackRun(runCorr); }
    }

    private async Task ExecuteFlowTrackedAsync(Guid flowId, Guid tenantId, string runCorr, string clientId,
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

                // Flujos con paso OTP (lectura de token por correo) NO se pueden compilar por tramos: el OTP
                // lo resuelve el runtime (no el navegador) y la sesion DIAN debe sobrevivir login->OTP->
                // descarga. Para esos flujos "Ejecutar todo" corre los pasos EN ORDEN por el MISMO camino del
                // paso a paso (RunStepNowAsync: intercepta el OTP, mantiene UNA sesion viva e inyecta URL_PASO
                // al siguiente paso). Asi el boton de arriba equivale a pulsar 1..N a mano y se puede agendar.
                if (steps.Any(s => s.Kind == ScrapeStepKind.LeerCorreoOtp))
                {
                    var notas = new List<string>();
                    var hechos = 0;
                    foreach (var paso in steps)
                    {
                        StepRunResult r;
                        try { r = await RunStepNowAsync(flowId, paso.Id, tenantId, CancellationToken.None); }
                        catch (Exception ex) { r = new StepRunResult(false, false, ex.Message, null, null, 0, 0, 0, ex.Message); }
                        hechos++;
                        ins += r.Inserted; upd += r.Updated; del += r.Deleted;

                        if (r.Offline)
                        {
                            await runLog.CloseAsync(runCorr, false, ins, upd, del,
                                $"El agente quedo offline en el paso '{paso.Name}'.");
                            return;
                        }
                        // El OTP es duro: sin token el login no sirve y lo que sigue es inutil -> abortar.
                        if (paso.Kind == ScrapeStepKind.LeerCorreoOtp && !r.Ok)
                        {
                            await runLog.CloseAsync(runCorr, false, ins, upd, del,
                                $"Paso '{paso.Name}': {r.Error ?? r.Detail ?? "no se obtuvo el token"}.");
                            return;
                        }
                        // Pasos guarda/driver (EXPLORACION/Variable sin filas) devuelven Ok=false de forma
                        // benigna: se anota y se sigue, igual que el operador en el paso a paso.
                        if (!r.Ok) { notas.Add(paso.Name); }
                    }
                    // Nota: el paso-bucle de descarga sigue bajando en 2do plano (setTimeouts en el navegador);
                    // la corrida se cierra aqui aunque las descargas continuen unos minutos mas.
                    var det = $"Flujo ejecutado paso a paso ({hechos} pasos)"
                        + (notas.Count > 0 ? $"; sin filas en: {string.Join(", ", notas)}" : "") + ".";
                    await runLog.CloseAsync(runCorr, true, ins, upd, del, det);
                    log.LogInformation("[NAV-RUN] corr={Corr} OK (stepwise/OTP) pasos={N}", runCorr, hechos);
                    return;
                }

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
                            var req = new BrowserRequestMsg(segCorr, tenantId.ToString(), compiled.Actions, DownloadFolder: DownloadFolderFrom(vars));
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
        // Las que siguen vivas en este proceso NO estan colgadas (flujo largo): no se tocan.
        stale = stale.Where(r => r.CorrelationId is null || !_activeRuns.ContainsKey(r.CorrelationId)).ToList();
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

    /// <summary>Carpeta destino de descargas definida por la config del flujo (variable RutaDescargaBot).
    /// Null/blanco => el agente baja a su carpeta por defecto (Downloads del usuario). Se envia al agente en
    /// la BrowserRequestMsg; el agente la aplica en DownloadStarting (compat: agente viejo la ignora).</summary>
    private static string? DownloadFolderFrom(IReadOnlyDictionary<string, string> vars)
        => vars.TryGetValue("RutaDescargaBot", out var f) && !string.IsNullOrWhiteSpace(f) ? f.Trim() : null;

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
