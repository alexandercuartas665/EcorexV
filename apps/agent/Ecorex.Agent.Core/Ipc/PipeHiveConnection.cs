using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Ipc;

/// <summary>
/// La colmena vista desde la UI cuando el dueno del canal es el Servicio (ADR-0039 s3). Implementa
/// <see cref="IHiveConnection"/>, el mismo seam que ya usaba la GUI en la Ola B: por eso el
/// ViewModel y el panal **no cambian**. Antes detras del seam habia un cliente SignalR; ahora hay un
/// pipe al servicio, que es quien conecta de verdad.
///
/// El estado que pinta el panal deja de ser propio y pasa a ser el REAL del servicio: si el servicio
/// esta Online, la colmena lo muestra Online aunque ella acabe de abrirse.
/// </summary>
public sealed class PipeHiveConnection : IHiveConnection, IAsyncDisposable
{
    private readonly AgentIpcClient _ipc;
    private readonly IBrowserSubAgent? _browser;
    private ConnectionState _state = ConnectionState.Offline;

    public PipeHiveConnection(IBrowserSubAgent? browser)
    {
        _browser = browser;
        _ipc = new AgentIpcClient(browser);
        _ipc.StateChanged += OnStateChanged;
        _ipc.RequestStarted += r => RequestStarted?.Invoke(r);
        _ipc.RequestFinished += r => RequestFinished?.Invoke(r);
        _ipc.ConnectivityChanged += connected =>
        {
            if (connected) { return; }
            // Sin servicio no hay agente: el panal debe verse Offline aunque la ventana este abierta,
            // y el Navegador vuelve a denegarlo todo (el permiso lo otorga el servicio; si no hay
            // quien lo sostenga, caduca).
            _browser?.ApplyPolicy(BrowserPolicy.Denied);
            SetState(ConnectionState.Offline);
        };
        _ipc.Acked += a => Acked?.Invoke(a);
    }

    public ConnectionState State => _state;

    public event Action<ConnectionState>? ConnectionChanged;
    public event Action<HiveRequest>? RequestStarted;
    public event Action<HiveRequestResult>? RequestFinished;

    /// <summary>Ultimo estado publicado por el servicio (identidad, allow-lists, consentimiento).</summary>
    public event Action<AgentIpc.StateMsg>? ServiceStateChanged;

    /// <summary>Respuesta a una mutacion (ej. rechazada por no ser administrador).</summary>
    public event Action<AgentIpc.AckMsg>? Acked;

    /// <summary>El servicio esta arriba y hablando con nosotros.</summary>
    public bool IsServiceUp => _ipc.IsConnected;

    public void Start() => _ipc.Start();

    private void OnStateChanged(AgentIpc.StateMsg s)
    {
        // El permiso del Navegador viaja con el estado: la colmena tiene el escritorio pero NO la
        // boveda, asi que no puede consultar por si misma si esta habilitado ni a que dominios ir.
        // Sin esto el navegador delegado falla cerrado siempre (visto el 2026-07-16).
        _browser?.ApplyPolicy(new BrowserPolicy(s.BrowserEnabled, s.BrowserAllow));

        SetState(s.Connection);
        ServiceStateChanged?.Invoke(s);
    }

    private void SetState(ConnectionState s)
    {
        if (_state == s) { return; }
        _state = s;
        ConnectionChanged?.Invoke(s);
    }

    /// <summary>
    /// "Probar conexion" desde la colmena = pedirle al servicio que guarde la identidad y reconecte.
    /// La colmena ya NO abre la boveda ni habla con el hub: solo lo pide. Si el usuario no es
    /// administrador, el servicio responde que no (llega por <see cref="Acked"/>).
    /// </summary>
    public async Task<bool> TestConnectionAsync(AgentConfig config, CancellationToken cancellationToken = default)
    {
        if (!_ipc.IsConnected) { return false; }
        await _ipc.SendAsync(AgentIpc.Kinds.SetConfig,
            new AgentIpc.SetConfigMsg(config.ClientId, config.HubUrl, config.Secret));
        // El resultado real llega asincrono por `state` (el servicio reconecta y publica): esto solo
        // dice que la orden salio.
        return true;
    }

    public Task RefreshAsync() => _ipc.SendAsync(AgentIpc.Kinds.GetState);

    public Task SetBrowserAllowAsync(IReadOnlyList<string> items) =>
        _ipc.SendAsync(AgentIpc.Kinds.SetBrowserAllow, new AgentIpc.SetAllowMsg(items));

    public Task SetFileAllowAsync(IReadOnlyList<string> items) =>
        _ipc.SendAsync(AgentIpc.Kinds.SetFileAllow, new AgentIpc.SetAllowMsg(items));

    public Task SetConsentAsync(bool browser, bool files) =>
        _ipc.SendAsync(AgentIpc.Kinds.SetConsent, new AgentIpc.SetConsentMsg(browser, files));

    public ValueTask DisposeAsync() => _ipc.DisposeAsync();
}
