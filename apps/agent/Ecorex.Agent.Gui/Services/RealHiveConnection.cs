using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using Ecorex.Contracts.Agent;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Ecorex.Agent.Gui.Services;

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

    private readonly SqlServerGatewayExecutor _sql = new();
    private readonly GatewaySourceStore _sources = new();
    private readonly WebView2BrowserSubAgent _browser = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HubConnection? _conn;
    private AgentConfig _config;
    private ConnectionState _state = ConnectionState.Offline;

    public RealHiveConnection(AgentConfig config) => _config = config;

    public ConnectionState State => _state;

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

            SetState(ConnectionState.Connecting);
            var conn = Build(config);
            WireHandlers(conn);
            _conn = conn;

            try
            {
                await conn.StartAsync(cancellationToken);
                SetState(ConnectionState.Online);
                await SafeHelloAsync(conn);
                return true;
            }
            catch
            {
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
    private static async Task<string?> AcquireTokenAsync(AgentConfig config)
    {
        try
        {
            var hub = new Uri(config.HubUrl);
            var tokenUrl = $"{hub.Scheme}://{hub.Authority}/api/agente/token";
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nonce = Guid.NewGuid().ToString("N");
            var hmac = AgentHmac.Compute(config.Secret, config.ClientId, ts, nonce);

            var resp = await Http.PostAsJsonAsync(tokenUrl, new AgentTokenRequest(config.ClientId, ts, nonce, hmac));
            if (!resp.IsSuccessStatusCode) { return null; }
            var body = await resp.Content.ReadFromJsonAsync<AgentTokenResponse>();
            return body?.AccessToken;
        }
        catch
        {
            return null; // sin token -> el hub [Authorize] rechaza -> queda Offline.
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
        conn.On<BrowserRequestMsg>(AgentHubMethods.BrowserRequest, req => OnBrowserRequestAsync(conn, req));
        conn.On(AgentHubMethods.Ping, () => SafeInvokeAsync(conn, AgentHubMethods.Heartbeat));
    }

    /// <summary>
    /// Traduce una orden real en la animacion de la colmena y la EJECUTA (Ola C, Database -> SQL
    /// Server de la LAN, solo-lectura + chunking). Otros conectores acusan recibo por ahora.
    /// </summary>
    private async Task OnFetchRequestAsync(HubConnection conn, FetchRequestMsg req)
    {
        var kind = MapKind(req.Connector);
        var detail = Shorten(req.Query?.Text) ?? req.Connector?.Kind;
        RequestStarted?.Invoke(new HiveRequest(req.CorrelationId, kind, detail));

        var isDatabase = string.Equals(req.Connector?.Kind, "Database", StringComparison.OrdinalIgnoreCase);
        try
        {
            if (isDatabase)
            {
                await ExecuteDatabaseAsync(conn, req);
            }
            else
            {
                await AckAsync(conn, req);
                RequestFinished?.Invoke(new HiveRequestResult(req.CorrelationId, Ok: true, "recibido"));
            }
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
    }

    /// <summary>Ejecuta la consulta contra la fuente SQL Server local y envia los chunks de FetchResult.</summary>
    private async Task ExecuteDatabaseAsync(HubConnection conn, FetchRequestMsg req)
    {
        var engine = req.Connector?.DbEngine ?? "SqlServer";
        if (!string.Equals(engine, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new GatewayException("UNSUPPORTED_ENGINE", $"Motor no soportado en Ola C: {engine}.");
        }

        var connectionString = _sources.LoadSqlServer();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new GatewayException("NO_SOURCE", "El agente no tiene una fuente SQL Server configurada.");
        }

        var query = req.Query ?? new QuerySpec(string.Empty);
        var total = 0;
        await foreach (var chunk in _sql.ExecuteAsync(connectionString, req.CorrelationId, query, req.Paging))
        {
            total += chunk.RowCount;
            await conn.InvokeAsync(AgentHubMethods.FetchResult, chunk);
        }
        RequestFinished?.Invoke(new HiveRequestResult(req.CorrelationId, Ok: true, $"{total} filas"));
    }

    /// <summary>
    /// Atiende una orden del sub-agente Navegador (doc 06 s3.2): enciende la celda, ejecuta la
    /// secuencia en el hilo de UI (WebView2) con allow-list, y devuelve BrowserResult.
    /// </summary>
    private async Task OnBrowserRequestAsync(HubConnection conn, BrowserRequestMsg req)
    {
        var detail = req.Actions.FirstOrDefault(a => a.Kind == BrowserActionKind.Navigate)?.Url ?? "navegador";
        RequestStarted?.Invoke(new HiveRequest(req.CorrelationId, SubAgentKind.Browser, Shorten(detail)));
        try
        {
            // WebView2 es un control de UI: se ejecuta en el Dispatcher.
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            var result = await dispatcher.InvokeAsync(() => _browser.ExecuteAsync(req)).Task.Unwrap();
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
