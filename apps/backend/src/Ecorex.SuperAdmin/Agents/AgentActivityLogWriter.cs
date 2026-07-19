using Ecorex.Application.Common;
using Ecorex.Domain.Entities;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>Lo minimo para registrar UNA orden atendida por un agente colmena. El TenantId viaja
/// explicito porque el registro se escribe FUERA del scope de la peticion (background del runtime),
/// donde ya no hay tenant ambiente.</summary>
public sealed record AgentActivityEntry(
    Guid TenantId,
    string ClientId,
    string? ClientName,
    AgentActivityKind Kind,
    string CorrelationId,
    string? Origin,
    bool Ok,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string? Detail);

/// <summary>
/// Escribe la bitacora transversal de actividad de los agentes colmena (ADR-0045, Ola 2). Best-effort:
/// si falla, se registra un warning pero NO se propaga -la bitacora nunca debe tumbar una orden real-.
/// Seguro desde singletons y desde el background del runtime: abre su propio scope por escritura.
/// </summary>
public interface IAgentActivityLog
{
    Task RecordAsync(AgentActivityEntry entry, CancellationToken ct = default);
}

public sealed class AgentActivityLogWriter(IServiceScopeFactory scopeFactory, ILogger<AgentActivityLogWriter> log)
    : IAgentActivityLog
{
    public async Task RecordAsync(AgentActivityEntry e, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var durationMs = (int)Math.Clamp((e.FinishedAt - e.StartedAt).TotalMilliseconds, 0, int.MaxValue);
            db.AgentActivityLogs.Add(new AgentActivityLog
            {
                TenantId = e.TenantId,
                ClientId = e.ClientId,
                ClientName = e.ClientName,
                Kind = e.Kind,
                CorrelationId = e.CorrelationId,
                Origin = Trim(e.Origin, 300),
                Result = e.Ok ? AgentActivityResult.Ok : AgentActivityResult.Error,
                StartedAt = e.StartedAt,
                FinishedAt = e.FinishedAt,
                DurationMs = durationMs,
                Detail = Trim(e.Detail, 600),
                CreatedAt = e.FinishedAt
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[AGENTE] no se pudo escribir la bitacora de actividad (best-effort) corr={Corr}", e.CorrelationId);
        }
    }

    private static string? Trim(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}
