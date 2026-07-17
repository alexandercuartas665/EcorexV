using Ecorex.Agent.Core.Services;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Ipc;

/// <summary>
/// El Navegador visto por el Servicio (ADR-0039 s4): el servicio no tiene escritorio, asi que **pide
/// prestado** el de una colmena por el pipe. Es una implementacion mas del seam
/// <see cref="IBrowserSubAgent"/>, de modo que el canal y el MCP no se enteran de nada: siguen
/// llamando `ExecuteAsync` como cuando WebView2 estaba en el mismo proceso.
///
/// Si no hay colmena abierta (servidor sin sesion), NO se cuelga la peticion: se responde con el
/// mismo NO explicito de <see cref="UnavailableBrowserSubAgent"/>. El Gateway y los Archivos, que si
/// son headless, siguen atendiendo con normalidad.
/// </summary>
public sealed class DelegatedBrowserSubAgent(AgentIpcServer ipc) : IBrowserSubAgent
{
    /// <summary>
    /// Tope de una secuencia de navegador delegada. Generoso (navegar + esperar condiciones + capturar
    /// es lento), pero finito: sin el, una colmena que muere a mitad dejaria al servidor esperando un
    /// acuse que no llegara nunca.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private readonly UnavailableBrowserSubAgent _noDesktop = new();

    /// <summary>
    /// La allow-list de dominios la evalua la colmena, que es quien ejecuta. El servicio no adivina.
    /// </summary>
    public bool IsAllowed(string? host) => false;

    /// <summary>
    /// El servicio no ejecuta el navegador: la politica viaja a la colmena junto con el estado y la
    /// aplica ella al navegar. Aqui no hay nada que guardar.
    /// </summary>
    public void ApplyPolicy(BrowserPolicy policy) { }

    public async Task<BrowserResultMsg> ExecuteAsync(BrowserRequestMsg request)
    {
        if (!ipc.HasBrowserProvider) { return await _noDesktop.ExecuteAsync(request); }

        var result = await ipc.DelegateBrowserAsync(request, Timeout);
        if (result is not null) { return result; }

        // La colmena se cerro entre el chequeo y el envio, o tardo mas del tope.
        var reason = "La colmena que atendia el Navegador no respondio (se cerro o excedio el tiempo).";
        return new BrowserResultMsg(request.CorrelationId, false,
            request.Actions.Select((a, i) => new BrowserActionResult(i, a.Kind, Ok: false, Error: reason)).ToList(),
            reason);
    }
}
