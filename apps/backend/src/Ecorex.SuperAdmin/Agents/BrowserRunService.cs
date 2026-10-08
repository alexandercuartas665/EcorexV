using System.Collections.Concurrent;
using System.Text.Json;
using Ecorex.Application.Automatizaciones.ConciliacionDian;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.Rules;
using Ecorex.Application.Scraping;
using Ecorex.Contracts.Agent;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Auth;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>Lo que devuelve "Ejecutar ahora": si se despacho, a que corrida corresponde, y si quedo
/// esperando al agente o fallo antes de salir.</summary>
public sealed record BrowserRunResult(bool Dispatched, Guid? RunId, string? CorrelationId, bool Offline, string? Error);

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

    /// <summary>Ejecuta UN solo paso del flujo contra la SESION VIVA del agente (modo "paso a paso"), de forma
    /// SINCRONA: despacha el paso con la clave de sesion del flujo (el agente mantiene el navegador abierto y
    /// reusa la pagina del paso anterior), ingiere lo que extraiga, lo registra en la bitacora y devuelve el
    /// resultado para pintarlo en la UI. Pensado para encadenar pasos a mano (login -> OTP -> continuar).</summary>
    Task<StepRunResult> RunStepNowAsync(Guid flowId, Guid stepId, Guid tenantId, CancellationToken ct = default);

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

    /// <summary>Variables de SESION del modo paso a paso, por flujo: valores efimeros producidos durante el
    /// stepping (p.ej. el token que leyo un paso OTP) que se superponen a las variables del flujo para los
    /// pasos siguientes, SIN persistirlos en BD. Se limpian al cerrar la sesion. Singleton -> estado vivo
    /// entre llamadas (cada paso es una invocacion aparte).</summary>
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, string>> _stepSessionVars = new();

    /// <summary>Clave de sesion viva del navegador para el stepping de un flujo (el agente reusa el mismo
    /// perfil/ventana entre pasos). Estable por flujo.</summary>
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

    // ---- Modo "paso a paso" (sesion viva): un paso a la vez, sincrono, sobre la MISMA ventana del agente ----

    public async Task<StepRunResult> RunStepNowAsync(Guid flowId, Guid stepId, Guid tenantId, CancellationToken ct = default)
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
                return await RunProcesarArchivosStepAsync(scope, db, flowId, tenantId, step, vars, ct);
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
        int EsperarAsentarSegundos = 30, int GraciaInicialSegundos = 150, int MaxEsperaSegundos = 900);

    /// <summary>Procesa (server-side) los archivos ya descargados: parsea cada ZIP (DIAN UBL), hace UPSERT por
    /// CUFE en la fuente Bot (sin duplicar), mueve los procesados y -si AutoImportar- los lleva al CCD del mes.
    /// La carpeta sale de la config o, por defecto, de la variable RutaDescargaBot del flujo.</summary>
    private async Task<StepRunResult> RunProcesarArchivosStepAsync(
        IServiceScope scope, IApplicationDbContext db, Guid flowId, Guid tenantId, ScrapeStep step,
        IReadOnlyDictionary<string, string> vars, CancellationToken ct)
    {
        ProcesarArchivosConfig cfg = new();
        if (!string.IsNullOrWhiteSpace(step.MappingJson))
        {
            try { cfg = JsonSerializer.Deserialize<ProcesarArchivosConfig>(step.MappingJson!,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? cfg; }
            catch { /* json viejo/invalido: defaults */ }
        }

        var carpeta = !string.IsNullOrWhiteSpace(cfg.Carpeta) ? ResolveVars(cfg.Carpeta!, vars)
            : (vars.TryGetValue("RutaDescargaBot", out var rd) && !string.IsNullOrWhiteSpace(rd) ? rd : null);
        if (string.IsNullOrWhiteSpace(carpeta) || !Directory.Exists(carpeta))
        {
            var m = $"No existe la carpeta de descargas: {carpeta ?? "(sin configurar; define RutaDescargaBot o Carpeta)"}";
            await RecordStepRunAsync(db, flowId, step.Name, false, 0, 0, 0, m, ct);
            return new StepRunResult(false, false, m, null, null, 0, 0, 0, m);
        }
        var patron = string.IsNullOrWhiteSpace(cfg.Patron) ? "*.zip" : cfg.Patron!;
        var nuestroNit = vars.TryGetValue("NIT_EMPRESA", out var ne) ? ne.Trim() : "";

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

        foreach (var archivo in archivos)
        {
            DianDoc? doc;
            try { doc = DianUblParser.ParseZip(archivo, nuestroNit); }
            catch { doc = null; }
            if (doc is null || string.IsNullOrWhiteSpace(doc.Cufe)) { fallidos++; continue; }

            var existente = await fdb.ConciliacionDianBotDummies.FirstOrDefaultAsync(b => b.Cufe == doc.Cufe, ct);
            if (existente is null)
            {
                fdb.ConciliacionDianBotDummies.Add(new ConciliacionDianBotDummy
                {
                    TenantId = tenantId, Cufe = doc.Cufe, TipoDoc = doc.TipoDoc, FechaDoc = doc.Fecha,
                    NombreEmisor = doc.NombreEmisor, NitEmisor = doc.NitEmisor, PrefijoFolio = doc.NumFactura,
                    IdCompra = "", SubtotalBruto = doc.SubtotalBruto, DescuentoComercial = doc.Descuento,
                    Subtotal = doc.SubtotalNeto, Iva = doc.Iva, TotalAntesRet = doc.TotalAntesRet,
                    RetencionFuente = doc.Retefuente, RetencionIca = doc.ReteIca, Total = doc.Total,
                    TipoPago = doc.TipoPago, ProveedorTecnologico = "",
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

        var detalle = $"Procesados {archivos.Length}: {nuevos} nuevos, {actualizados} actualizados"
            + (fallidos > 0 ? $", {fallidos} con error" : "")
            + (cfg.AutoImportar ? $"; {importados} importados a conciliacion ({periodos.Count} periodo/s)" : "")
            + (cfg.MoverProcesados ? "; movidos a /procesados" : "") + ".";
        await RecordStepRunAsync(fdb, flowId, step.Name, true, nuevos, actualizados, 0, detalle, ct);
        return new StepRunResult(true, false, null, null, null, nuevos, actualizados, 0, detalle);
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
            int ins = 0, upd = 0, del = 0, rowsOk = 0, pagesDone = 0;
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
            var page = 1;
            while (true)
            {
                var rows = CurrentRows();
                for (var ri = 0; ri < rows.Count; ri++)
                {
                    ctx.Set("ROW", rows[ri]);
                    ctx.Set("ROW_INDEX", ri.ToString());
                    var rowOk = true;
                    foreach (var step in body) { if (!await ExecStepAsync(step)) { rowOk = false; break; } }
                    if (rowOk) { rowsOk++; } else { break; }
                }
                pagesDone++;
                if (lastErr is not null || !paginate) { break; }

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
                + (lastErr is null ? "" : $"; ultimo error: {lastErr}");
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
            flow.LastResultSummary = text.Length <= 600 ? text : text[..597] + "...";
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
        Script = ctx.Substitute(s.Script),
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
