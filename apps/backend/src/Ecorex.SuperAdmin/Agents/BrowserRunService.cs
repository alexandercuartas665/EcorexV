using System.Collections.Concurrent;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Contracts.Agent;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Auth;
using Ecorex.SuperAdmin.RealTime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>Lo que devuelve "Ejecutar ahora": si se despacho, a que corrida corresponde, y si quedo
/// esperando al agente o fallo antes de salir.</summary>
public sealed record BrowserRunResult(bool Dispatched, Guid? RunId, string? CorrelationId, bool Offline, string? Error);

/// <summary>
/// Runtime DETERMINISTA de los flujos de extraccion (doc 03 s1, Ola 3). Espejo de
/// <see cref="IAgentImportService"/> pero para el sub-agente Navegador: compila el flujo a
/// <see cref="BrowserAction"/>[] (firmando el JS), abre la corrida, comprueba que el agente este en
/// linea y empuja un <c>BrowserRequest</c>; cuando el Navegador responde por el hub, correlaciona por
/// correlationId, ingiere las filas de los pasos Extract en la tabla destino (reusando
/// <see cref="IRowIngestService"/>) y cierra la corrida en la bitacora.
///
/// Este servicio es el que llena el hueco que <c>AgenteHub.BrowserResult</c> dejaba: hasta ahora el
/// resultado del Navegador solo se logueaba; ahora se correlaciona y se ingiere, como el FetchResult.
/// Singleton (mantiene las peticiones pendientes entre invocaciones del hub); la ingesta corre en un
/// scope propio con el tenant fijado.
/// </summary>
public interface IBrowserRunService
{
    /// <summary>Dispara un flujo AHORA ("Ejecutar ahora"). Abre la corrida, y despacha o la deja
    /// PendingOffline si el agente no esta. No espera el resultado (llega despues, por el hub).</summary>
    Task<BrowserRunResult> RunFlowNowAsync(Guid flowId, Guid tenantId, ImportRunTrigger trigger, CancellationToken ct = default);

    /// <summary>Resultado del Navegador (lo llama el hub). Ingiere los Extract y cierra la corrida.</summary>
    Task OnBrowserResultAsync(BrowserResultMsg msg);

    /// <summary>Cierra las peticiones vencidas (agente que se cayo a mitad). Lo llama el worker.</summary>
    Task SweepAsync(CancellationToken ct = default);
}

public sealed class BrowserRunService : IBrowserRunService
{
    /// <summary>Cuanto se espera al Navegador antes de dar la corrida por perdida. Un flujo con esperas
    /// y varias paginas puede tardar; generoso a proposito.</summary>
    private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(10);

    private readonly IHubContext<AgenteHub> _hub;
    private readonly IAgentRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BrowserRunService> _log;
    private readonly TimeProvider _clock;

    private readonly ConcurrentDictionary<string, Pending> _pending = new();

    public BrowserRunService(IHubContext<AgenteHub> hub, IAgentRegistry registry,
        IServiceScopeFactory scopeFactory, ILogger<BrowserRunService> log, TimeProvider? clock = null)
    {
        _hub = hub;
        _registry = registry;
        _scopeFactory = scopeFactory;
        _log = log;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<BrowserRunResult> RunFlowNowAsync(Guid flowId, Guid tenantId, ImportRunTrigger trigger,
        CancellationToken ct = default)
    {
        var corr = NewCorrelationId();
        var firedAt = _clock.GetUtcNow();

        using var scope = _scopeFactory.CreateScope();
        using (AmbientTenantContext.Begin(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var runLog = scope.ServiceProvider.GetRequiredService<IScrapeFlowRunLog>();
            var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();

            var flow = await db.ScrapeFlows
                .Include(f => f.Steps)
                .Include(f => f.Variables)
                .FirstOrDefaultAsync(f => f.Id == flowId, ct);
            if (flow is null)
            {
                return new BrowserRunResult(false, null, null, false, "El flujo no existe o no es de este tenant.");
            }

            // La corrida se abre ANTES de cualquier validacion que pueda fallar, para que TODO intento
            // quede en la bitacora (incluido "no tiene agente" o "no compila"): un disparo sin rastro es
            // justo lo que la bitacora existe para evitar.
            var runId = await runLog.OpenAsync(flowId, trigger, firedAt, corr, flow.Steps.Count, ct);

            // Agente asignado (identidad publica + secreto para firmar el JS).
            if (flow.ClientId is not Guid clientPk)
            {
                await runLog.FailAsync(runId, "El flujo no tiene un agente asignado.", ct);
                return new BrowserRunResult(false, runId, corr, false, "El flujo no tiene un agente asignado.");
            }
            var client = await db.DataClients.FirstOrDefaultAsync(c => c.Id == clientPk && c.IsActive, ct);
            if (client is null)
            {
                await runLog.FailAsync(runId, "El agente asignado no existe o esta inactivo.", ct);
                return new BrowserRunResult(false, runId, corr, false, "El agente asignado no existe o esta inactivo.");
            }

            string? secret = null;
            if (client.ClientSecretEncrypted is not null)
            {
                try { secret = protector.Unprotect(client.ClientSecretEncrypted); } catch { /* ilegible */ }
            }

            var variables = DecryptVariables(flow.Variables, protector);

            CompiledFlow compiled;
            try
            {
                compiled = ScrapeFlowCompiler.Compile(flow, variables, corr, secret);
            }
            catch (ScrapeCompileException ex)
            {
                await runLog.FailAsync(runId, ex.Message, ct);
                return new BrowserRunResult(false, runId, corr, false, ex.Message);
            }
            if (compiled.Actions.Count == 0)
            {
                await runLog.FailAsync(runId, "El flujo no tiene pasos que ejecutar.", ct);
                return new BrowserRunResult(false, runId, corr, false, "El flujo no tiene pasos que ejecutar.");
            }

            // Agente en linea? Si no, se deja esperando (PendingOffline): en Ola 5 el horario lo reintenta
            // al reconectar, igual que el Contenedor de datos.
            if (!_registry.IsOnline(client.ClientId))
            {
                await runLog.MarkOfflineAsync(runId, "El agente asignado no estaba en linea. La corrida queda esperando.", ct);
                return new BrowserRunResult(false, runId, corr, true, null);
            }

            // Se registra la peticion ANTES de empujarla, para que un agente veloz no responda antes de
            // que el servidor sepa que enganches de ingesta tenia esta orden.
            _pending[corr] = new Pending(client.ClientId, tenantId, compiled.Extracts, _clock.GetUtcNow() + PendingTtl);

            var req = new BrowserRequestMsg(corr, tenantId.ToString(), compiled.Actions);
            await _hub.Clients.Group(AgenteHub.ClientGroup(client.ClientId))
                .SendAsync(AgentHubMethods.BrowserRequest, req, ct);
            _log.LogInformation("[NAV-RUN] dispatch corr={Corr} flow={Flow} client={Client} actions={N} extracts={E}",
                corr, flowId, client.ClientId, compiled.Actions.Count, compiled.Extracts.Count);

            return new BrowserRunResult(true, runId, corr, false, null);
        }
    }

    public async Task OnBrowserResultAsync(BrowserResultMsg msg)
    {
        if (!_pending.TryRemove(msg.CorrelationId, out var p))
        {
            return; // no es una corrida de flujo (o ya cerrada/vencida).
        }

        using var scope = _scopeFactory.CreateScope();
        using (AmbientTenantContext.Begin(p.TenantId))
        {
            var runLog = scope.ServiceProvider.GetRequiredService<IScrapeFlowRunLog>();

            if (!msg.Ok)
            {
                var err = msg.Error ?? FirstError(msg) ?? "El Navegador reporto un error.";
                await runLog.CloseAsync(msg.CorrelationId, false, 0, 0, 0, err);
                return;
            }

            try
            {
                var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var ingest = scope.ServiceProvider.GetRequiredService<IRowIngestService>();
                int ins = 0, upd = 0, del = 0;

                foreach (var bind in p.Extracts)
                {
                    var res = msg.Results.FirstOrDefault(r => r.Index == bind.ActionIndex);
                    if (res is null || !res.Ok)
                    {
                        throw new InvalidOperationException(
                            $"El paso de extraccion #{bind.ActionIndex + 1} no devolvio datos: {res?.Error ?? "sin resultado"}.");
                    }

                    var rows = ParseRows(res.Value);
                    var mapping = await BuildMappingAsync(db, bind, p.TenantId, CancellationToken.None);
                    if (mapping.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "El mapeo del paso Extraer no apunta a ninguna columna de la tabla destino.");
                    }

                    // Append: cada corrida agrega lo extraido. El modo por paso (Replace/Upsert) es
                    // configuracion que llega en una ola posterior; por ahora se acumula, que nunca
                    // pierde datos.
                    var session = ingest.CreateSession(bind.TargetContainerId, p.TenantId, mapping, ApiImportMode.Append, null);
                    await session.PrepareAsync(CancellationToken.None);
                    await session.IngestChunkAsync(rows, CancellationToken.None);
                    ins += session.Inserted; upd += session.Updated; del += session.Deleted;
                }

                var detail = p.Extracts.Count == 0
                    ? "Secuencia ejecutada (sin pasos de extraccion)."
                    : $"{ins} filas extraidas.";
                await runLog.CloseAsync(msg.CorrelationId, true, ins, upd, del, detail);
                _log.LogInformation("[NAV-RUN] corr={Corr} OK ins={Ins} upd={Upd} del={Del}", msg.CorrelationId, ins, upd, del);
            }
            catch (Exception ex)
            {
                await runLog.CloseAsync(msg.CorrelationId, false, 0, 0, 0, ex.Message);
                _log.LogError(ex, "[NAV-RUN] corr={Corr} fallo la ingesta", msg.CorrelationId);
            }
        }
    }

    public async Task SweepAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        foreach (var (corr, p) in _pending)
        {
            if (p.DeadlineUtc > now) { continue; }
            if (!_pending.TryRemove(corr, out _)) { continue; }

            var detail = $"El Navegador no respondio en {PendingTtl.TotalMinutes:0} minutos.";
            _log.LogWarning("[NAV-RUN] corr={Corr} vencio el plazo; se descarta la peticion", corr);
            await PushCancelAsync(p.ClientId, corr, "timeout", ct);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                using (AmbientTenantContext.Begin(p.TenantId))
                {
                    var runLog = scope.ServiceProvider.GetRequiredService<IScrapeFlowRunLog>();
                    await runLog.CloseAsync(corr, false, 0, 0, 0, detail, ct);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[NAV-RUN] corr={Corr} no se pudo cerrar la corrida vencida", corr);
            }
            if (ct.IsCancellationRequested) { return; }
        }
    }

    private async Task PushCancelAsync(string clientId, string correlationId, string reason, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.Group(AgenteHub.ClientGroup(clientId))
                .SendAsync(AgentHubMethods.Cancel, new CancelMsg(correlationId, reason), ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[NAV-RUN] corr={Corr} no se pudo empujar el Cancel", correlationId);
        }
    }

    /// <summary>Descifra las variables del flujo a nombre->valor. Las no-secretas van en claro; las
    /// secretas se descifran (si alguna esta corrupta, se omite en vez de tumbar toda la corrida).</summary>
    private static Dictionary<string, string> DecryptVariables(
        IEnumerable<Domain.Entities.ScrapeVariable> vars, ISecretProtector protector)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in vars)
        {
            if (string.IsNullOrEmpty(v.ValueEncrypted)) { continue; }
            if (v.IsSecret)
            {
                try { dict[v.Name] = protector.Unprotect(v.ValueEncrypted); } catch { /* omite la corrupta */ }
            }
            else
            {
                dict[v.Name] = v.ValueEncrypted;
            }
        }
        return dict;
    }

    /// <summary>Construye el mapeo columnId -> campo-del-resultado que consume IRowIngestService. Si el
    /// paso trae MappingJson (campo->nombreColumna), se invierte resolviendo el nombre a columnId; si no
    /// trae mapeo, se asume identidad (cada columna se llena con el campo del mismo nombre).</summary>
    private static async Task<Dictionary<Guid, string>> BuildMappingAsync(
        IApplicationDbContext db, ExtractBinding bind, Guid tenantId, CancellationToken ct)
    {
        var columns = await db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == bind.TargetContainerId && (
                c.Type == DataContainerColumnType.Text || c.Type == DataContainerColumnType.Number ||
                c.Type == DataContainerColumnType.Decimal || c.Type == DataContainerColumnType.Date ||
                c.Type == DataContainerColumnType.Boolean))
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        if (columns.Count == 0) { return new Dictionary<Guid, string>(); }

        var byName = columns.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var mapping = new Dictionary<Guid, string>();

        if (string.IsNullOrWhiteSpace(bind.MappingJson))
        {
            // Identidad: la columna "PRECIO" se llena con el campo "PRECIO" del resultado.
            foreach (var c in columns) { mapping[c.Id] = c.Name; }
            return mapping;
        }

        // Mapeo explicito: { "campoEnElResultado": "NOMBRE_COLUMNA", ... }
        Dictionary<string, string>? pairs = null;
        try { pairs = JsonSerializer.Deserialize<Dictionary<string, string>>(bind.MappingJson); } catch { /* invalido */ }
        if (pairs is null) { return mapping; }
        foreach (var (field, colName) in pairs)
        {
            if (!string.IsNullOrWhiteSpace(colName) && byName.TryGetValue(colName, out var colId))
            {
                mapping[colId] = field;
            }
        }
        return mapping;
    }

    /// <summary>Convierte el Value de un Eval (que WebView2 serializa a JSON) en filas campo->valor. El
    /// script del paso Extraer debe evaluar a un arreglo de objetos planos; si por error llega como
    /// cadena JSON (doble codificada), se desanida una vez.</summary>
    public static List<IReadOnlyDictionary<string, string?>> ParseRows(string? value)
    {
        var rows = new List<IReadOnlyDictionary<string, string?>>();
        if (string.IsNullOrWhiteSpace(value)) { return rows; }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(value);
            root = doc.RootElement.Clone();
        }
        catch { return rows; }

        // Doble codificacion: el Value es una cadena que a su vez contiene el JSON del arreglo.
        if (root.ValueKind == JsonValueKind.String)
        {
            var inner = root.GetString();
            if (string.IsNullOrWhiteSpace(inner)) { return rows; }
            try { using var doc2 = JsonDocument.Parse(inner); root = doc2.RootElement.Clone(); }
            catch { return rows; }
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            rows.Add(ToRow(root));
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in root.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Object) { rows.Add(ToRow(el)); }
            }
        }
        return rows;
    }

    private static IReadOnlyDictionary<string, string?> ToRow(JsonElement obj)
    {
        var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in obj.EnumerateObject())
        {
            row[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Null => null,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => prop.Value.GetRawText()
            };
        }
        return row;
    }

    private static string? FirstError(BrowserResultMsg msg) =>
        msg.Results.FirstOrDefault(r => !r.Ok)?.Error;

    private static string NewCorrelationId() => Guid.NewGuid().ToString("N")[..8];

    private sealed record Pending(string ClientId, Guid TenantId,
        IReadOnlyList<ExtractBinding> Extracts, DateTimeOffset DeadlineUtc);
}
