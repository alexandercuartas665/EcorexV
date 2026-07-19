using Ecorex.Agent.Core.Ipc;
using Ecorex.Agent.Core.Services;
using Ecorex.Contracts.Agent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ecorex.Agent.Service;

/// <summary>
/// El corazon del servicio (ADR-0039): levanta el canal con la identidad de la boveda y lo mantiene
/// vivo. Es deliberadamente delgado: toda la logica (canal, Gateway, Archivos, allow-lists) vive en
/// `Ecorex.Agent.Core`, el MISMO codigo que hospeda la colmena WPF. Aqui solo se decide el "quien soy
/// y cuando arranco".
///
/// Sin configuracion (equipo recien instalado) NO se cae: registra el motivo y reintenta, porque el
/// operador puede configurar el ClientId despues desde la colmena. Una vez conectado, la reconexion
/// con backoff es asunto de RealHiveConnection.
/// </summary>
public sealed class AgentWorker(ILogger<AgentWorker> logger) : BackgroundService
{
    private static readonly TimeSpan WaitForConfig = TimeSpan.FromSeconds(30);

    private RealHiveConnection? _hive;
    private AgentIpcServer? _ipc;

    /// <summary>
    /// Se dispara cuando la colmena manda una identidad nueva por el pipe: hay que soltar el canal
    /// actual y rearmarlo con ella, sin esperar los 30s del reintento.
    /// </summary>
    private readonly SemaphoreSlim _configChanged = new(0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Agente ECOREX: servicio iniciado. Boveda: {Dir}", AgentVault.Dir);

        var store = new DpapiConfigStore();

        // Canal local con las colmenas (ADR-0039 s3): les publica el estado real, acepta cambios de
        // configuracion (de administradores) y les delega el Navegador.
        _ipc = new AgentIpcServer(
            () => new AgentIpcServer.StateMsgSource(_hive?.State ?? ConnectionState.Offline, _hive?.LastError),
            _ => _configChanged.Release(),
            m => logger.LogInformation("{Mensaje}", m));
        _ipc.Start();
        logger.LogInformation("Canal local escuchando en \\\\.\\pipe\\{Pipe} (la colmena se conecta aqui).", AgentIpc.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            var config = store.Load();
            logger.LogInformation(
                "Config leida de la boveda: ClientId={ClientId} Hub={Hub} Secreto={Secreto} Completa={Completa}",
                string.IsNullOrWhiteSpace(config.ClientId) ? "(vacio)" : config.ClientId,
                string.IsNullOrWhiteSpace(config.HubUrl) ? "(vacio)" : config.HubUrl,
                config.HasSecret ? "si" : "NO",
                config.IsComplete);
            if (!config.IsComplete)
            {
                logger.LogWarning(
                    "Sin configuracion en la boveda (ClientId/URL). Configure el agente desde la colmena; se reintenta en {Segundos}s.",
                    WaitForConfig.TotalSeconds);
                await WaitAsync(WaitForConfig, stoppingToken);
                continue;
            }

            try
            {
                // El Navegador exige escritorio y el servicio no lo tiene: se lo pide prestado a una
                // colmena por el pipe. Sin colmena, responde NO explicito (no cuelga la peticion).
                // Gateway y Archivos, headless, atienden aqui mismo.
                _hive = new RealHiveConnection(config, new DelegatedBrowserSubAgent(_ipc));
                _hive.ConnectionChanged += s =>
                {
                    logger.LogInformation("Canal: {Estado}", s);
                    _ = _ipc.BroadcastStateAsync(); // el panal de la colmena se pinta solo
                };
                _hive.RequestStarted += r =>
                {
                    logger.LogInformation("Orden {Id}: {Kind} {Detalle}", r.CorrelationId, r.Kind, r.Detail);
                    _ = _ipc.PublishRequestStartedAsync(r);
                };
                _hive.RequestFinished += r =>
                {
                    logger.LogInformation("Orden {Id}: {Resultado} {Detalle}",
                        r.CorrelationId, r.Ok ? "OK" : "ERROR", r.Detail);
                    _ = _ipc.PublishRequestFinishedAsync(r);
                };

                var ok = await _hive.TestConnectionAsync(config, stoppingToken);
                await _ipc.BroadcastStateAsync();
                if (!ok)
                {
                    logger.LogWarning("No se pudo conectar a {Url} con ClientId {ClientId}. Motivo: {Motivo}. Se reintenta.",
                        config.HubUrl, config.ClientId, _hive.LastError ?? "desconocido");
                    await DisposeHiveAsync();
                    await WaitAsync(WaitForConfig, stoppingToken);
                    continue;
                }

                logger.LogInformation("Conectado a {Url} como {ClientId}. Atendiendo Gateway y Archivos.",
                    config.HubUrl, config.ClientId);

                // Conectado: SignalR reconecta con backoff por su cuenta. El worker solo despierta si
                // se apaga el servicio o si una colmena manda una identidad nueva por el pipe (y
                // entonces hay que rearmar el canal con ella, no seguir con la vieja).
                await _configChanged.WaitAsync(stoppingToken);
                logger.LogInformation("La identidad cambio desde la colmena: se rearma el canal.");
                await DisposeHiveAsync();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // apagado normal del servicio
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fallo del canal; se reintenta en {Segundos}s.", WaitForConfig.TotalSeconds);
                await DisposeHiveAsync();
                await WaitAsync(WaitForConfig, stoppingToken);
            }
        }

        await DisposeHiveAsync();
        if (_ipc is not null) { await _ipc.DisposeAsync(); }
        logger.LogInformation("Agente ECOREX: servicio detenido.");
    }

    /// <summary>
    /// Espera antes de reintentar, pero DESPIERTA de inmediato si la colmena manda una identidad
    /// nueva: sin esto, configurar el agente y quedarse mirando hasta 30s a que "reaccione" seria una
    /// mala experiencia. No explota al apagar el servicio (el token cancela a proposito).
    /// </summary>
    private async Task WaitAsync(TimeSpan delay, CancellationToken ct)
    {
        try { await _configChanged.WaitAsync(delay, ct); }
        catch (OperationCanceledException) { /* apagado */ }
    }

    private async Task DisposeHiveAsync()
    {
        if (_hive is null) { return; }
        try { await _hive.DisposeAsync(); } catch { /* best-effort */ }
        _hive = null;
    }
}
