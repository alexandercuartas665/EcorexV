using Ecorex.Application.DataContainers;
using Ecorex.Contracts.Agent;
using Ecorex.Domain.Entities;
using Ecorex.SuperAdmin.Agents;
using Ecorex.SuperAdmin.RealTime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// El camino de DATOS via agente (dispatch de FetchRequest + desenlace por el hub) era INVISIBLE en la
/// bitacora de actividad del agente (agent_activity_logs): solo el sub-agente Navegador la escribia. Sin
/// ese rastro, una orden despachada que el agente no completaba no dejaba ninguna huella ("dispatched"
/// sin Ok ni Error). Estas pruebas fijan que AHORA cada desenlace escribe UNA fila Kind=Fetch.
/// </summary>
public sealed class AgentFetchActivityLogTests
{
    [Fact]
    public async Task FetchFailed_registra_actividad_Fetch_con_Error()
    {
        var activity = new CapturingActivityLog();
        var svc = new AgentImportService(new NoopHubContext(), new EmptyScopeFactory(),
            activity, NullLogger<AgentImportService>.Instance);

        var tenant = Guid.NewGuid();
        var container = Guid.NewGuid();
        var corr = await svc.DispatchFetchAsync(
            clientId: "cli_test", tenantId: tenant, containerId: container,
            mapping: new Dictionary<Guid, string>(), mode: ApiImportMode.Upsert, keyColumnId: null,
            query: "REST", connector: null, ct: default, correlationId: "corr0001", rest: null);

        await svc.OnFetchFailedAsync(new FetchErrorMsg(corr, "REST_HTTP", "500 boom", Retryable: false));

        var entry = Assert.Single(activity.Entries);
        Assert.Equal(AgentActivityKind.Fetch, entry.Kind);
        Assert.False(entry.Ok);
        Assert.Equal("cli_test", entry.ClientId);
        Assert.Equal(corr, entry.CorrelationId);
        Assert.Equal(tenant, entry.TenantId);
        Assert.Contains("REST_HTTP", entry.Detail);
    }

    [Fact]
    public async Task FetchFailed_sin_peticion_pendiente_no_registra()
    {
        // Un FetchFailed cuyo correlationId no corresponde a una ingesta (o ya cerrada) no debe dejar fila.
        var activity = new CapturingActivityLog();
        var svc = new AgentImportService(new NoopHubContext(), new EmptyScopeFactory(),
            activity, NullLogger<AgentImportService>.Instance);

        await svc.OnFetchFailedAsync(new FetchErrorMsg("desconocido", "X", "y", Retryable: false));

        Assert.Empty(activity.Entries);
    }

    // ---- Fakes minimos (el proyecto no usa libreria de mocking) ----

    private sealed class CapturingActivityLog : IAgentActivityLog
    {
        public List<AgentActivityEntry> Entries { get; } = new();
        public Task RecordAsync(AgentActivityEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopHubContext : IHubContext<AgenteHub>
    {
        public IHubClients Clients { get; } = new NoopClients();
        public IGroupManager Groups { get; } = new NoopGroups();
    }

    private sealed class NoopClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new NoopProxy();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class NoopProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoopGroups : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    // Scope vacio: CloseRunAsync intenta resolver IImportRunLog y falla, pero su try/catch lo traga; el
    // registro de actividad (que va por el IAgentActivityLog inyectado, no por el scope) SI ocurre.
    private sealed class EmptyScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type serviceType) => null;
        public void Dispose() { }
    }
}
