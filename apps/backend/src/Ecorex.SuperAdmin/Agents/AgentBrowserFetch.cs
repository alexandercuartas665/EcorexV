using Ecorex.Application.Common;
using Ecorex.Application.Workflows;
using Ecorex.Contracts.Agent;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Implementacion real de <see cref="IAgentBrowserFetch"/> (ADR-0091): navega a una URL con un cliente
/// COLMENA on-prem y devuelve su contenido legible, para que un agente de flujo llene el formulario con
/// datos de la web. Reusa el canal sincrono <see cref="IBrowserActionChannel"/> (Navigate + ExtractReadable,
/// una sola orden acotada). No firma nada (Navigate/ExtractReadable no requieren firma, a diferencia de
/// Eval/Mouse). NUNCA lanza: cliente inexistente/offline o timeout vienen como Ok=false con motivo.
/// </summary>
public sealed class AgentBrowserFetch : IAgentBrowserFetch
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(50);
    private const int MaxContentChars = 14_000;

    private readonly IApplicationDbContext _db;
    private readonly IBrowserActionChannel _channel;
    private readonly IAgentRegistry _registry;

    // Varias 'buscar_web' del mismo paso pueden entrar a la vez (ADR-0091 paralelo). El DbContext NO es
    // thread-safe: se serializa SOLO la unica lectura de BD (resolver el cliente Colmena, ~ms). La parte cara
    // -la orden al navegador por el canal- queda FUERA del candado y sigue corriendo en paralelo real.
    private readonly SemaphoreSlim _dbGate = new(1, 1);

    public AgentBrowserFetch(IApplicationDbContext db, IBrowserActionChannel channel, IAgentRegistry registry)
    {
        _db = db;
        _channel = channel;
        _registry = registry;
    }

    public async Task<AgentBrowserFetchResult> FetchAsync(
        Guid clientId, string? sessionKey, string url, string? selector, Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return AgentBrowserFetchResult.Fail("La URL a buscar no es valida (debe ser http/https completa).");
        }

        // El cliente Colmena del tenant (filtro global). Se guarda su Guid en el nodo; aqui se resuelve al
        // ClientId publico con el que se dirige la orden al agente on-prem.
        DataClient? client;
        await _dbGate.WaitAsync(cancellationToken);
        try
        {
            client = await _db.DataClients.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);
        }
        finally { _dbGate.Release(); }
        if (client is null)
        {
            return AgentBrowserFetchResult.Fail("El cliente Colmena configurado en el paso ya no existe.");
        }
        if (!client.IsActive)
        {
            return AgentBrowserFetchResult.Fail($"El cliente Colmena '{client.Name}' esta inactivo.");
        }
        if (!_registry.IsOnline(client.ClientId))
        {
            return AgentBrowserFetchResult.Fail(
                $"El agente Colmena '{client.Name}' no esta conectado; no se puede buscar en la web ahora.");
        }

        var corr = Guid.NewGuid().ToString("N")[..8];
        var actions = new List<BrowserAction>
        {
            new(BrowserActionKind.Navigate, Url: url),
            new(BrowserActionKind.ExtractReadable, Selector: string.IsNullOrWhiteSpace(selector) ? null : selector,
                ScrollRounds: 3, WaitMs: 1200),
        };
        var request = new BrowserRequestMsg(corr, tenantId.ToString(), actions,
            SessionKey: string.IsNullOrWhiteSpace(sessionKey) ? null : sessionKey);

        BrowserResultMsg result;
        try
        {
            result = await _channel.ExecuteAsync(client.ClientId, request, Timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return AgentBrowserFetchResult.Fail("El navegador Colmena no respondio a tiempo.");
        }
        catch (Exception ex)
        {
            return AgentBrowserFetchResult.Fail($"Error usando el navegador Colmena: {ex.Message}");
        }

        var nav = result.Results.FirstOrDefault(r => r.Kind == BrowserActionKind.Navigate);
        if (nav is { Ok: false })
        {
            return AgentBrowserFetchResult.Fail("No se pudo abrir la URL: " + (nav.Error ?? "navegacion no permitida"));
        }
        var extract = result.Results.FirstOrDefault(r => r.Kind == BrowserActionKind.ExtractReadable);
        var value = extract?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return AgentBrowserFetchResult.Fail(
                extract?.Error ?? result.Error ?? "La pagina no devolvio contenido legible.");
        }

        return AgentBrowserFetchResult.Content_(
            value.Length > MaxContentChars ? value[..MaxContentChars] + "...[recortado]" : value);
    }
}
