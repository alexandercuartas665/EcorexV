using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Navegador NO disponible: lo usa el Worker Service, que corre sin escritorio (ADR-0039 s4). WebView2
/// es un control visual y no vive en la sesion 0 de un servicio, asi que una orden de navegador que
/// llega al servicio en un equipo SIN colmena abierta se responde con un NO explicito y accionable.
///
/// La regla que hace cumplir: **fallar claro, nunca colgar**. El servidor recibe su `BrowserResult`
/// con el motivo real y el `FetchRequest` no queda esperando un acuse que no va a llegar. Gateway y
/// Archivos, que si son headless, siguen atendiendo con normalidad.
///
/// En la Ola 5c esto lo reemplaza una implementacion que DELEGA por named pipe a la colmena cuando
/// hay una sesion interactiva; esta clase queda como el caso "no hay nadie que preste el escritorio".
/// </summary>
public sealed class UnavailableBrowserSubAgent : IBrowserSubAgent
{
    private const string Reason =
        "El Navegador exige una sesion interactiva de Windows y el agente corre como servicio. " +
        "Abra la colmena ECOREX en la sesion del equipo para atender ordenes de navegador.";

    public bool IsAllowed(string? host) => false;

    /// <summary>No hay navegador que gobernar: la politica no aplica aqui.</summary>
    public void ApplyPolicy(BrowserPolicy policy) { }

    public Task<BrowserResultMsg> ExecuteAsync(BrowserRequestMsg request)
    {
        var results = request.Actions
            .Select((a, i) => new BrowserActionResult(i, a.Kind, Ok: false, Error: Reason))
            .ToList();
        return Task.FromResult(new BrowserResultMsg(request.CorrelationId, false, results, Reason));
    }
}
