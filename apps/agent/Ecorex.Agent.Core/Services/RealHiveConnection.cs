using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using Ecorex.Contracts.Agent;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Ola B: implementacion REAL de <see cref="IHiveConnection"/> sobre SignalR (doc 02). El agente
/// INICIA la conexion saliente al hub del servidor, saluda con <c>AgentHello</c>, y traduce las
/// ordenes <c>FetchRequest</c> empujadas por el servidor en eventos que la colmena ya sabe pintar
/// (RequestStarted -> worker efimero -> RequestFinished). La GUI y el ViewModel NO cambian: solo se
/// sustituye el mock por esta clase detras de la misma interfaz.
///
/// Alcance Ola B: canal + protocolo + ciclo de vida (conexion/reconexion). La EJECUCION real de la
/// consulta contra la BD/API de la LAN es la Ola C; aqui el agente responde un acuse (FetchResult
/// vacio con estado) para cerrar el round-trip del canal.
/// </summary>
public sealed class RealHiveConnection : IHiveConnection, IAsyncDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly GatewayExecutor _gateway = new();
    private readonly GatewaySourceStore _sources = new();
    private readonly IBrowserSubAgent _browser;
    private readonly FileSubAgent _files = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Consultas en curso, por correlationId, para poder abortarlas cuando llega un <c>Cancel</c>. Sin
    /// esto el agente no tiene forma de "encontrar" la consulta que el servidor quiere parar. Se llena
    /// al empezar un FetchRequest y se vacia en el finally, pase lo que pase.
    /// </summary>
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inflight = new();

    private HubConnection? _conn;
    private AgentConfig _config;
    private ConnectionState _state = ConnectionState.Offline;

    public RealHiveConnection(AgentConfig config, IBrowserSubAgent browser)
    {
        _config = config;
        _browser = browser;
    }

    public ConnectionState State => _state;

    /// <summary>
    /// Motivo del ultimo fallo de conexion (null si la ultima conexion fue bien). Existe porque el
    /// servicio corre headless: sin esto, un fallo de handshake o una URL mal escrita se ven igual
    /// ("Offline") y no hay forma de diagnosticar en la maquina del cliente.
    /// </summary>
    public string? LastError { get; private set; }

    public event Action<ConnectionState>? ConnectionChanged;
    public event Action<HiveRequest>? RequestStarted;
    public event Action<HiveRequestResult>? RequestFinished;

    /// <summary>
    /// "Probar conexion" real: (re)conecta al hub de <paramref name="config"/>. Si ya habia una
    /// conexion (a otra URL), la cierra primero. Devuelve true si quedo en linea.
    /// </summary>
    public async Task<bool> TestConnectionAsync(AgentConfig config, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _config = config;
            await StopInternalAsync();

            if (!config.IsComplete)
            {
                SetState(ConnectionState.Offline);
                return false;
            }

            LastError = null; // cada intento cuenta su propia historia
            SetState(ConnectionState.Connecting);
            var conn = Build(config);
            WireHandlers(conn);
            _conn = conn;

            try
            {
                await conn.StartAsync(cancellationToken);
                LastError = null;
                SetState(ConnectionState.Online);
                await SafeHelloAsync(conn);
                return true;
            }
            catch (Exception ex)
            {
                // El motivo NO se puede perder: en un equipo on-prem, sin escritorio y sin nadie
                // mirando, "no pude conectar" a secas es indepurable. Quien hospeda (servicio o
                // colmena) decide como mostrarlo. `??=`: si el handshake ya explico el porque, ese
                // motivo es mas util que el "401" pelado con el que StartAsync se queja aqui.
                LastError ??= ex.Message;
                SetState(ConnectionState.Offline);
                return false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private HubConnection Build(AgentConfig config)
    {
        return new HubConnectionBuilder()
            .WithUrl(config.HubUrl, options =>
            {
                // doc 02 s1: transporte forzado a WebSockets.
                options.Transports = HttpTransportType.WebSockets;
                // doc 02 s2 (opcion A): si hay secreto, el hub exige JWT -> se adquiere un token corto
                // por HMAC en /api/agente/token y se pasa por AccessTokenProvider. Sin secreto se
                // conecta anonimo (util contra el simulador de dev).
                if (config.HasSecret)
                {
                    options.AccessTokenProvider = () => AcquireTokenAsync(config);
                }
            })
            .WithAutomaticReconnect(new HiveRetryPolicy())
            .Build();
    }

    /// <summary>Handshake opcion A (doc 02 s2): prueba el secreto con HMAC y obtiene un JWT corto.</summary>
    private async Task<string?> AcquireTokenAsync(AgentConfig config)
    {
        try
        {
            var hub = new Uri(config.HubUrl);
            var tokenUrl = $"{hub.Scheme}://{hub.Authority}/api/agente/token";
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nonce = Guid.NewGuid().ToString("N");
            var hmac = AgentHmac.Compute(config.Secret, config.ClientId, ts, nonce);

            var resp = await Http.PostAsJsonAsync(tokenUrl, new AgentTokenRequest(config.ClientId, ts, nonce, hmac));
            if (!resp.IsSuccessStatusCode)
            {
                // Este es EL fallo mas probable en campo (secreto cambiado, ClientId que ya no existe,
                // reloj del equipo desfasado >120s). Sin esto, arriba solo se ve un 401 pelado.
                var detail = await resp.Content.ReadAsStringAsync();
                LastError = $"El handshake fue rechazado ({(int)resp.StatusCode} {resp.StatusCode}) por {tokenUrl}: {detail.Trim()}";
                return null;
            }
            var body = await resp.Content.ReadFromJsonAsync<AgentTokenResponse>();
            return body?.AccessToken;
        }
        catch (Exception ex)
        {
            LastError = $"No se pudo pedir el token a {config.HubUrl}: {ex.Message}";
            return null; // sin token -> el hub [Authorize] rechaza -> queda Offline
        }
    }

    private void WireHandlers(HubConnection conn)
    {
        // Ciclo de vida -> estado de conexion de la colmena.
        conn.Reconnecting += _ => { SetState(ConnectionState.Connecting); return Task.CompletedTask; };
        conn.Reconnected += async _ => { SetState(ConnectionState.Online); await SafeHelloAsync(conn); };
        conn.Closed += _ => { SetState(ConnectionState.Offline); return Task.CompletedTask; };

        // Servidor -> agente (doc 02 s4).
        conn.On<FetchRequestMsg>(AgentHubMethods.FetchRequest, req => OnFetchRequestAsync(conn, req));
        // CONCURRENCIA: cada orden de navegador corre en su propia Task y NO bloquea el bucle de recepcion
        // de SignalR. Asi varias ordenes simultaneas (p.ej. 4 flujos disparados a la vez) llegan a la colmena
        // al mismo tiempo y su pool abre una WebView2 aislada por orden -en paralelo- en lugar de serializarse
        // aqui. El pipe y el pool ya soportan varias en vuelo (correlacion por CorrelationId).
        conn.On<BrowserRequestMsg>(AgentHubMethods.BrowserRequest, req =>
        {
            _ = Task.Run(() => OnBrowserRequestAsync(conn, req));
            return Task.CompletedTask;
        });
        conn.On<FileRequestMsg>(AgentHubMethods.FileRequest, req => OnFileRequestAsync(conn, req));
        conn.On<CancelMsg>(AgentHubMethods.Cancel, msg => OnCancel(msg));
        conn.On(AgentHubMethods.Ping, () => SafeInvokeAsync(conn, AgentHubMethods.Heartbeat));
    }

    /// <summary>
    /// Traduce una orden real en la animacion de la colmena y la EJECUTA (Ola C, Database -> SQL
    /// Server de la LAN, solo-lectura + chunking). Otros conectores acusan recibo por ahora.
    /// </summary>
    /// <summary>Aborta la consulta en curso con este correlationId, si la hay. Best-effort: si ya
    /// termino (o nunca existio), no pasa nada. Lo dispara un <c>Cancel</c> del servidor.</summary>
    private void OnCancel(CancelMsg msg)
    {
        if (_inflight.TryGetValue(msg.CorrelationId, out var cts))
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { /* ya termino: nada que cancelar */ }
        }
    }

    private async Task OnFetchRequestAsync(HubConnection conn, FetchRequestMsg req)
    {
        var kind = MapKind(req.Connector);
        var detail = Shorten(req.Query?.Text) ?? req.Connector?.Kind;
        RequestStarted?.Invoke(new HiveRequest(req.CorrelationId, kind, detail));

        // Un CTS por correlationId: es lo que el Cancel del servidor podra disparar. Se limpia en el
        // finally pase lo que pase, para no dejar fugas ni cancelar una consulta futura por error.
        var cts = new CancellationTokenSource();
        _inflight[req.CorrelationId] = cts;

        var isDatabase = string.Equals(req.Connector?.Kind, "Database", StringComparison.OrdinalIgnoreCase);
        try
        {
            if (isDatabase)
            {
                await ExecuteDatabaseAsync(conn, req, cts.Token);
            }
            else
            {
                await AckAsync(conn, req);
                RequestFinished?.Invoke(new HiveRequestResult(req.CorrelationId, Ok: true, "recibido"));
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Cancelacion PEDIDA por el servidor: no es un error del agente. Se avisa con un codigo
            // propio y retryable:false (no tiene sentido reintentar algo que se pidio abortar).
            await SafeInvokeAsync(conn, AgentHubMethods.FetchFailed,
                new FetchErrorMsg(req.CorrelationId, "CANCELLED", "Cancelado por el servidor.", Retryable: false));
            RequestFinished?.Invoke(new HiveRequestResult(req.CorrelationId, Ok: false, "cancelado"));
        }
        catch (GatewayException gx)
        {
            await SafeInvokeAsync(conn, AgentHubMethods.FetchFailed,
                new FetchErrorMsg(req.CorrelationId, gx.Code, gx.Message, gx.Retryable));
            RequestFinished?.Invoke(new HiveRequestResult(req.CorrelationId, Ok: false, gx.Message));
        }
        catch (Exception ex)
        {
            await SafeInvokeAsync(conn, AgentHubMethods.FetchFailed,
                new FetchErrorMsg(req.CorrelationId, "AGENT_ERROR", ex.Message, Retryable: true));
            RequestFinished?.Invoke(new HiveRequestResult(req.CorrelationId, Ok: false, ex.Message));
        }
        finally
        {
            _inflight.TryRemove(req.CorrelationId, out _);
            cts.Dispose();
        }
    }

    /// <summary>
    /// Ejecuta la consulta contra la fuente y envia los chunks de FetchResult.
    ///
    /// De donde sale la credencial (ADR-0040): si el `ConnectorSpec` trae `Secret`, manda el servidor
    /// (opcion a, lo configurado en el modulo web). Si no, se usa la fuente LOCAL del agente (opcion
    /// b, la de la Ola C): asi un agente ya configurado a mano sigue funcionando sin tocar nada.
    /// </summary>
    private async Task ExecuteDatabaseAsync(HubConnection conn, FetchRequestMsg req, CancellationToken ct)
    {
        var engine = req.Connector?.DbEngine ?? "SqlServer";
        if (!GatewayExecutor.IsSupported(engine))
        {
            throw new GatewayException("UNSUPPORTED_ENGINE",
                $"El agente no sabe hablar con el motor '{engine}'. Soportados: SqlServer, PostgreSql.");
        }

        var connectionString = GatewayExecutor.BuildConnectionString(req.Connector)
                               ?? _sources.LoadSqlServer();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new GatewayException("NO_SOURCE",
                "No hay credencial: ni el servidor la mando en el conector, ni el agente tiene una fuente configurada.");
        }

        var query = req.Query ?? new QuerySpec(string.Empty);
        var total = 0;
        // El token viaja hasta OpenAsync/ExecuteReaderAsync/ReadAsync del GatewayExecutor (que ya lo
        // honra): un Cancel del servidor aborta la consulta en la BD, no solo el bucle de envio.
        await foreach (var chunk in _gateway.ExecuteAsync(engine, connectionString, req.CorrelationId, query, req.Paging, ct))
        {
            total += chunk.RowCount;
            await conn.InvokeAsync(AgentHubMethods.FetchResult, chunk, ct);
        }
        RequestFinished?.Invoke(new HiveRequestResult(req.CorrelationId, Ok: true, $"{total} filas"));
    }

    /// <summary>
    /// Atiende una orden del sub-agente Navegador (doc 06 s3.2): enciende la celda, ejecuta la
    /// secuencia con allow-list, y devuelve BrowserResult. Que el navegador sea WebView2 y necesite
    /// hilo de UI es asunto de la implementacion del seam (ADR-0039), no de este canal.
    /// </summary>
    private async Task OnBrowserRequestAsync(HubConnection conn, BrowserRequestMsg req)
    {
        var detail = req.Actions.FirstOrDefault(a => a.Kind == BrowserActionKind.Navigate)?.Url ?? "navegador";
        RequestStarted?.Invoke(new HiveRequest(req.CorrelationId, SubAgentKind.Browser, Shorten(detail)));

        // Endurecimiento (doc 06 s4): el JS empujado por el servidor (Eval/Mouse/Wait-condicion) debe
        // venir FIRMADO por el secreto del cliente. Fail-closed: sin firma valida no se ejecuta nada.
        var (signed, reason) = VerifyBrowserSignatures(req);
        if (!signed)
        {
            await SafeInvokeAsync(conn, AgentHubMethods.BrowserResult,
                new BrowserResultMsg(req.CorrelationId, false, Array.Empty<BrowserActionResult>(), reason));
            RequestFinished?.Invoke(new HiveRequestResult(req.CorrelationId, false, reason));
            return;
        }

        try
        {
            var result = await _browser.ExecuteAsync(req);
            await conn.InvokeAsync(AgentHubMethods.BrowserResult, result);
            RequestFinished?.Invoke(new HiveRequestResult(req.CorrelationId, result.Ok,
                result.Ok ? $"{result.Results.Count} acciones" : result.Results.FirstOrDefault(r => !r.Ok)?.Error));
        }
        catch (Exception ex)
        {
            await SafeInvokeAsync(conn, AgentHubMethods.BrowserResult,
                new BrowserResultMsg(req.CorrelationId, false, Array.Empty<BrowserActionResult>(), ex.Message));
            RequestFinished?.Invoke(new HiveRequestResult(req.CorrelationId, false, ex.Message));
        }
    }

    /// <summary>
    /// Verifica que las acciones que inyectan JS del servidor lleven una firma HMAC valida (doc 06 s4).
    /// El JS por MCP local NO pasa por aqui (confianza loopback). Fail-closed si no hay secreto local.
    /// </summary>
    private (bool Ok, string? Reason) VerifyBrowserSignatures(BrowserRequestMsg req)
    {
        foreach (var a in req.Actions)
        {
            var payload = a.Kind switch
            {
                BrowserActionKind.Eval => a.Script,
                BrowserActionKind.Mouse => a.ScriptJson,
                BrowserActionKind.Wait when !string.IsNullOrWhiteSpace(a.ConditionScript) => a.ConditionScript,
                _ => null,
            };
            if (payload is null) { continue; } // accion sin JS del servidor (navigate/screenshot/html/...)

            if (string.IsNullOrEmpty(_config.Secret))
            {
                return (false, "Sin secreto local: no se puede verificar la firma del JS del servidor.");
            }
            if (!AgentSign.Verify(_config.Secret, req.CorrelationId, payload, a.Signature))
            {
                return (false, $"Firma de JS invalida o ausente para la accion {a.Kind}.");
            }
        }
        return (true, null);
    }

    /// <summary>Atiende una orden del sub-agente Archivos (doc 06 s3.2): acotada a la allow-list de rutas.</summary>
    private async Task OnFileRequestAsync(HubConnection conn, FileRequestMsg req)
    {
        var detail = req.Actions.FirstOrDefault()?.Path ?? "archivos";
        RequestStarted?.Invoke(new HiveRequest(req.CorrelationId, SubAgentKind.Files, Shorten(detail)));
        try
        {
            var result = await _files.ExecuteAsync(req);
            await conn.InvokeAsync(AgentHubMethods.FileResult, result);
            RequestFinished?.Invoke(new HiveRequestResult(req.CorrelationId, result.Ok,
                result.Ok ? $"{result.Results.Count} acciones" : result.Results.FirstOrDefault(r => !r.Ok)?.Error));
        }
        catch (Exception ex)
        {
            await SafeInvokeAsync(conn, AgentHubMethods.FileResult,
                new FileResultMsg(req.CorrelationId, false, Array.Empty<FileActionResult>(), ex.Message));
            RequestFinished?.Invoke(new HiveRequestResult(req.CorrelationId, false, ex.Message));
        }
    }

    /// <summary>Acuse para conectores sin ejecutor propio (RestApi, etc.): cierra el canal.</summary>
    private static async Task AckAsync(HubConnection conn, FetchRequestMsg req)
    {
        await Task.Delay(500);
        var ack = new FetchResultMsg(
            req.CorrelationId, ChunkIndex: 0, IsLast: true,
            Fields: new[] { "_status" },
            Rows: new List<Dictionary<string, string?>> { new() { ["_status"] = "agent-online: sin ejecutor para este conector" } },
            RowCount: 0);
        await conn.InvokeAsync(AgentHubMethods.FetchResult, ack);
    }

    private async Task SafeHelloAsync(HubConnection conn)
    {
        var hello = new AgentHelloMsg(
            ClientId: _config.ClientId,
            AgentVersion: "1.0.0-olaB",
            ProtocolVersion: AgentProtocol.Version,
            Host: Environment.MachineName,
            Os: RuntimeInformation.OSDescription,
            Capabilities: new[] { "Database", "RestApi" });
        await SafeInvokeAsync(conn, AgentHubMethods.AgentHello, hello);
    }

    private static async Task SafeInvokeAsync(HubConnection conn, string method, object? arg = null)
    {
        try
        {
            if (conn.State != HubConnectionState.Connected) { return; }
            if (arg is null) { await conn.InvokeAsync(method); }
            else { await conn.InvokeAsync(method, arg); }
        }
        catch
        {
            // best-effort: el hub puede no implementar el metodo o la conexion caerse.
        }
    }

    private static SubAgentKind MapKind(ConnectorSpec? connector) => connector?.Kind switch
    {
        "RestApi" => SubAgentKind.Browser,
        _ => SubAgentKind.Gateway, // Database (y por defecto) -> Gateway de datos.
    };

    private static string? Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return null; }
        text = text.Trim().Replace('\n', ' ').Replace('\r', ' ');
        return text.Length <= 40 ? text : text[..40] + "...";
    }

    private void SetState(ConnectionState state)
    {
        if (_state == state) { return; }
        _state = state;
        ConnectionChanged?.Invoke(state);
    }

    private async Task StopInternalAsync()
    {
        var conn = _conn;
        _conn = null;
        if (conn is null) { return; }
        try { await conn.StopAsync(); } catch { /* best-effort */ }
        await conn.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { await StopInternalAsync(); }
        finally { _gate.Release(); _gate.Dispose(); }
    }
}

/// <summary>Reconexion con backoff de doc 02 s1: 0s, 2s, 5s, 10s, 30s, luego cada 60s.</summary>
public sealed class HiveRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Steps =
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    };

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        var i = retryContext.PreviousRetryCount;
        return i < Steps.Length ? Steps[i] : TimeSpan.FromSeconds(60);
    }
}
