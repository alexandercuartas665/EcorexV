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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Agente ECOREX: servicio iniciado. Boveda: {Dir}", AgentVault.Dir);

        var store = new DpapiConfigStore();

        while (!stoppingToken.IsCancellationRequested)
        {
            var config = store.Load();
            if (!config.IsComplete)
            {
                logger.LogWarning(
                    "Sin configuracion en la boveda (ClientId/URL). Configure el agente desde la colmena; se reintenta en {Segundos}s.",
                    WaitForConfig.TotalSeconds);
                await DelayAsync(WaitForConfig, stoppingToken);
                continue;
            }

            try
            {
                // El Navegador exige escritorio: en el servicio no existe. Falla claro (Ola 5c lo
                // reemplaza por la delegacion a la colmena). Gateway y Archivos si atienden aqui.
                _hive = new RealHiveConnection(config, new UnavailableBrowserSubAgent());
                _hive.ConnectionChanged += s => logger.LogInformation("Canal: {Estado}", s);
                _hive.RequestStarted += r => logger.LogInformation("Orden {Id}: {Kind} {Detalle}", r.CorrelationId, r.Kind, r.Detail);
                _hive.RequestFinished += r => logger.LogInformation("Orden {Id}: {Resultado} {Detalle}",
                    r.CorrelationId, r.Ok ? "OK" : "ERROR", r.Detail);

                var ok = await _hive.TestConnectionAsync(config, stoppingToken);
                if (!ok)
                {
                    logger.LogWarning("No se pudo conectar a {Url} con ClientId {ClientId}. Motivo: {Motivo}. Se reintenta.",
                        config.HubUrl, config.ClientId, _hive.LastError ?? "desconocido");
                    await DisposeHiveAsync();
                    await DelayAsync(WaitForConfig, stoppingToken);
                    continue;
                }

                logger.LogInformation("Conectado a {Url} como {ClientId}. Atendiendo Gateway y Archivos.",
                    config.HubUrl, config.ClientId);

                // Conectado: SignalR se encarga de reconectar con backoff. El worker solo espera el
                // apagado del servicio.
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // apagado normal del servicio
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fallo del canal; se reintenta en {Segundos}s.", WaitForConfig.TotalSeconds);
                await DisposeHiveAsync();
                await DelayAsync(WaitForConfig, stoppingToken);
            }
        }

        await DisposeHiveAsync();
        logger.LogInformation("Agente ECOREX: servicio detenido.");
    }

    /// <summary>Espera que NO explota al apagar el servicio (el token cancela el delay a proposito).</summary>
    private static async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) { /* apagado */ }
    }

    private async Task DisposeHiveAsync()
    {
        if (_hive is null) { return; }
        try { await _hive.DisposeAsync(); } catch { /* best-effort */ }
        _hive = null;
    }
}
