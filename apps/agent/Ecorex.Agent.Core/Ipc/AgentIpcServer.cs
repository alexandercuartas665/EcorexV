using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Ecorex.Agent.Core.Services;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Ipc;

/// <summary>
/// Extremo SERVIDOR del canal local (ADR-0039 s3): vive en el Servicio, que es el dueno de la
/// identidad, del canal y de la boveda. Atiende a las colmenas que se conecten: les publica el
/// estado, acepta sus cambios de configuracion (si son de un administrador) y les DELEGA el Navegador.
///
/// Puede haber mas de una colmena conectada (varias sesiones en el equipo): el estado se publica a
/// todas y una peticion de Navegador va a la primera que se ofrecio a servirlo.
/// </summary>
public sealed class AgentIpcServer : IAsyncDisposable
{
    /// <summary>Buffer del pipe. Ver la nota de CreatePipe: en 0, el canal se abraza a si mismo.</summary>
    private const int BufferSize = 64 * 1024;

    private readonly ConcurrentDictionary<Guid, ClientConn> _clients = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BrowserResultMsg>> _pendingBrowser = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<StateMsgSource> _stateSource;
    private readonly Action<AgentConfig> _onConfigChanged;

    private sealed record ClientConn(IpcChannel Channel, bool IsAdmin)
    {
        public bool CanServeBrowser { get; set; }
    }

    /// <summary>Lo que el servicio sabe y la colmena necesita para pintarse.</summary>
    public sealed record StateMsgSource(ConnectionState Connection, string? LastError);

    /// <param name="stateSource">Como preguntarle al servicio por su estado vivo del canal.</param>
    /// <param name="onConfigChanged">Aviso al servicio de que la identidad cambio (debe reconectar).</param>
    /// <param name="log">Bitacora del canal local (quien entra, quien sale, que se rechaza).</param>
    public AgentIpcServer(Func<StateMsgSource> stateSource, Action<AgentConfig> onConfigChanged, Action<string>? log = null)
    {
        _stateSource = stateSource;
        _onConfigChanged = onConfigChanged;
        _log = log ?? (_ => { });
    }

    private readonly Action<string> _log;

    /// <summary>Hay alguna colmena que pueda prestar su escritorio al Navegador.</summary>
    public bool HasBrowserProvider => _clients.Values.Any(c => c.CanServeBrowser && c.Channel.IsConnected);

    public void Start() => _ = Task.Run(AcceptLoopAsync);

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(_cts.Token);
                _ = Task.Run(() => ServeAsync(pipe));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Si esto falla, la colmena no puede hablar con el servicio y el agente pierde su
                // cara. Callarlo (como hacia antes) deja el sintoma "la colmena no conecta" sin causa.
                _log($"ERROR aceptando en el canal local: {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(1000, CancellationToken.None); // no girar en vacio si el pipe falla
            }
        }
    }

    /// <summary>
    /// El ACL del pipe deja CONECTAR a usuarios autenticados (la colmena corre como el operador, sin
    /// elevar). Eso NO es permiso para mutar: lo sensible se comprueba por impersonacion en
    /// <see cref="RequireAdmin"/>. Aqui solo se abre la puerta; adentro se pregunta quien eres.
    ///
    /// `Synchronize` es OBLIGATORIO y no viene en `ReadWrite`: sin el, el cliente no puede esperar en
    /// el handle y `CreateFile` le responde ACCESS_DENIED. Y ojo con el sintoma, que enganya:
    /// `NamedPipeClientStream.Connect` REINTENTA ante ACCESS_DENIED hasta agotar el plazo, asi que un
    /// ACL corto no se ve como "acceso denegado" sino como un **timeout** (verificado aqui el
    /// 2026-07-16: la colmena se quedaba fuera y el servicio la daba por ausente).
    /// </summary>
    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();

        // Los operadores solo CONECTAN: ni crean instancias ni tocan el ACL.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));

        // Quien HOSPEDA el servicio necesita FullControl, y no por lujo: para anadir instancias al
        // pipe (una por colmena) Windows exige `CreateNewInstance` sobre el DACL ya existente. La
        // primera instancia se crea sin consultar nada, asi que faltando este derecho el canal
        // funciona una vez y la siguiente colmena se queda fuera con ACCESS_DENIED (verificado el
        // 2026-07-16). En produccion el servicio es LocalSystem; en diagnostico corre como un
        // administrador: ambos deben poder.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        // Los buffers NO pueden ser 0. Con buffer cero, cada escritura espera a que el otro extremo
        // lea, y el saludo se abraza a si mismo: el servidor escribe `state` mientras el cliente
        // escribe `hello`, los dos bloqueados escribiendo y ninguno leyendo (verificado el
        // 2026-07-16: la colmena conectaba y el canal quedaba mudo). Con buffer, los mensajes chicos
        // salen sin bloquear y cada lado tiene su bucle de lectura para lo grande (capturas).
        return NamedPipeServerStreamAcl.Create(
            AgentIpc.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, BufferSize, BufferSize, security);
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        var id = Guid.NewGuid();
        var isAdmin = IsClientAdmin(pipe);
        var channel = new IpcChannel(pipe);
        var conn = new ClientConn(channel, isAdmin);
        _clients[id] = conn;
        _log($"Colmena conectada al canal local (administrador: {(isAdmin ? "si" : "no")}).");

        try
        {
            await SendStateAsync(conn);
            while (!_cts.IsCancellationRequested)
            {
                var msg = await channel.ReceiveAsync(_cts.Token);
                if (msg is null) { break; } // la colmena se cerro
                await HandleAsync(conn, msg);
            }
        }
        catch (Exception ex)
        {
            _log($"Canal local con la colmena roto: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _clients.TryRemove(id, out _);
            channel.Dispose();
            try { pipe.Dispose(); } catch { /* best-effort */ }
            _log("Colmena desconectada del canal local.");
        }
    }

    /// <summary>
    /// Quien esta del otro lado del pipe. Se impersona al cliente y se mira si su token es de
    /// administrador. Un usuario sin privilegios puede leer estado y servir el Navegador, pero no
    /// ensanchar una allow-list ni cambiar la identidad del agente.
    /// </summary>
    private static bool IsClientAdmin(NamedPipeServerStream pipe)
    {
        try
        {
            var isAdmin = false;
            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            });
            return isAdmin;
        }
        catch
        {
            return false; // fail-closed
        }
    }

    private async Task HandleAsync(ClientConn conn, AgentIpc.Envelope msg)
    {
        switch (msg.Kind)
        {
            case AgentIpc.Kinds.Hello:
                conn.CanServeBrowser = AgentIpc.Deserialize<AgentIpc.HelloMsg>(msg.Payload)?.CanServeBrowser ?? false;
                _log($"Colmena lista (presta escritorio al Navegador: {(conn.CanServeBrowser ? "si" : "no")}).");
                await SendStateAsync(conn);
                break;

            case AgentIpc.Kinds.GetState:
                await SendStateAsync(conn);
                break;

            case AgentIpc.Kinds.SetConfig:
                if (await RequireAdmin(conn, msg)) { break; }
                var cfg = AgentIpc.Deserialize<AgentIpc.SetConfigMsg>(msg.Payload);
                if (cfg is not null)
                {
                    var store = new DpapiConfigStore();
                    // Secreto vacio = "no lo cambies": la colmena nunca lo lee, asi que no puede
                    // reenviarlo, y sin esto un cambio de URL borraria la credencial.
                    var secret = string.IsNullOrEmpty(cfg.Secret) ? store.Load().Secret : cfg.Secret;
                    var updated = new AgentConfig(cfg.ClientId, cfg.HubUrl, secret);
                    store.Save(updated);
                    _onConfigChanged(updated);
                }
                await AckAsync(conn, msg.Id, true);
                await BroadcastStateAsync();
                break;

            case AgentIpc.Kinds.SetBrowserAllow:
                if (await RequireAdmin(conn, msg)) { break; }
                new BrowserAllowList().Save(AgentIpc.Deserialize<AgentIpc.SetAllowMsg>(msg.Payload)?.Items ?? []);
                await AckAsync(conn, msg.Id, true);
                await BroadcastStateAsync();
                break;

            case AgentIpc.Kinds.SetFileAllow:
                if (await RequireAdmin(conn, msg)) { break; }
                new FileAllowList().Save(AgentIpc.Deserialize<AgentIpc.SetAllowMsg>(msg.Payload)?.Items ?? []);
                await AckAsync(conn, msg.Id, true);
                await BroadcastStateAsync();
                break;

            case AgentIpc.Kinds.SetConsent:
                if (await RequireAdmin(conn, msg)) { break; }
                var consent = AgentIpc.Deserialize<AgentIpc.SetConsentMsg>(msg.Payload);
                if (consent is not null)
                {
                    var c = new CapabilityConsent();
                    c.SetBrowser(consent.Browser);
                    c.SetFiles(consent.Files);
                }
                await AckAsync(conn, msg.Id, true);
                await BroadcastStateAsync();
                break;

            case AgentIpc.Kinds.BrowserResult:
                var result = AgentIpc.Deserialize<BrowserResultMsg>(msg.Payload);
                if (result is not null && _pendingBrowser.TryRemove(result.CorrelationId, out var tcs))
                {
                    tcs.TrySetResult(result);
                }
                break;
        }
    }

    /// <summary>true si se RECHAZO (el llamador debe cortar). Ver nota de seguridad en AgentIpc.</summary>
    private async Task<bool> RequireAdmin(ClientConn conn, AgentIpc.Envelope msg)
    {
        if (conn.IsAdmin) { return false; }
        await AckAsync(conn, msg.Id, false,
            "Cambiar la configuracion del agente exige permisos de administrador en este equipo.");
        return true;
    }

    private async Task AckAsync(ClientConn conn, string? id, bool ok, string? error = null)
    {
        try
        {
            await conn.Channel.SendAsync(new AgentIpc.Envelope(
                AgentIpc.Kinds.Ack, id, AgentIpc.Serialize(new AgentIpc.AckMsg(ok, error))), _cts.Token);
        }
        catch { /* colmena cerrada */ }
    }

    private static AgentIpc.StateMsg BuildState(StateMsgSource live)
    {
        var config = new DpapiConfigStore().Load();
        var consent = new CapabilityConsent();
        return new AgentIpc.StateMsg(
            live.Connection, config.ClientId, config.HubUrl, config.HasSecret, live.LastError,
            consent.IsBrowserEnabled(), consent.IsFilesEnabled(),
            new BrowserAllowList().Load(), new FileAllowList().Load());
    }

    private Task SendStateAsync(ClientConn conn) => SendAsync(conn,
        new AgentIpc.Envelope(AgentIpc.Kinds.State, null, AgentIpc.Serialize(BuildState(_stateSource()))));

    /// <summary>Publica el estado a todas las colmenas (el panal se pinta solo, sin sondeo).</summary>
    public async Task BroadcastStateAsync()
    {
        var payload = AgentIpc.Serialize(BuildState(_stateSource()));
        var env = new AgentIpc.Envelope(AgentIpc.Kinds.State, null, payload);
        foreach (var c in _clients.Values) { await SendAsync(c, env); }
    }

    /// <summary>Evento del canal -> celda que enciende en el panal de todas las colmenas.</summary>
    public async Task PublishRequestStartedAsync(HiveRequest req) =>
        await BroadcastAsync(new AgentIpc.Envelope(AgentIpc.Kinds.RequestStarted, null, AgentIpc.Serialize(req)));

    public async Task PublishRequestFinishedAsync(HiveRequestResult res) =>
        await BroadcastAsync(new AgentIpc.Envelope(AgentIpc.Kinds.RequestFinished, null, AgentIpc.Serialize(res)));

    private async Task BroadcastAsync(AgentIpc.Envelope env)
    {
        foreach (var c in _clients.Values) { await SendAsync(c, env); }
    }

    private async Task SendAsync(ClientConn conn, AgentIpc.Envelope env)
    {
        try { await conn.Channel.SendAsync(env, _cts.Token); }
        catch { /* colmena cerrada: su ServeAsync la retira */ }
    }

    /// <summary>
    /// Delega una orden de Navegador a una colmena con escritorio y espera su resultado. Si no hay
    /// ninguna, devuelve null y el llamador responde el NO explicito (nunca se cuelga la peticion).
    /// </summary>
    public async Task<BrowserResultMsg?> DelegateBrowserAsync(BrowserRequestMsg req, TimeSpan timeout)
    {
        var target = _clients.Values.FirstOrDefault(c => c.CanServeBrowser && c.Channel.IsConnected);
        if (target is null) { return null; }

        var tcs = new TaskCompletionSource<BrowserResultMsg>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingBrowser[req.CorrelationId] = tcs;
        try
        {
            await target.Channel.SendAsync(new AgentIpc.Envelope(
                AgentIpc.Kinds.BrowserRequest, req.CorrelationId, AgentIpc.Serialize(req)), _cts.Token);

            // Si la colmena se cierra a mitad de una navegacion, nadie respondera: el timeout es lo
            // que impide que el servidor quede esperando un acuse para siempre.
            using var timeoutCts = new CancellationTokenSource(timeout);
            return await tcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch
        {
            return null;
        }
        finally
        {
            _pendingBrowser.TryRemove(req.CorrelationId, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        foreach (var c in _clients.Values) { c.Channel.Dispose(); }
        _clients.Clear();
        _cts.Dispose();
    }
}
