using Ecorex.Agent.HubSim;
using Ecorex.Contracts.Agent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddHostedService<FetchPump>();

// Puerto fijo para que el agente apunte a http://localhost:5280/hubs/agente.
builder.WebHost.UseUrls("http://localhost:5280");

var app = builder.Build();

app.MapHub<AgenteHub>(AgentProtocol.HubRoute);
app.MapGet("/", () => Results.Text(
    $"ECOREX Hub Simulator (Ola B). Hub en {AgentProtocol.HubRoute} - protocolo v{AgentProtocol.Version}. " +
    "Empuja FetchRequest a los agentes conectados."));

app.Run();
