using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Agents;

/// <summary>Una fila del feed de actividad, ya lista para pintar.</summary>
public sealed record AgentActivityDto(
    Guid Id,
    string ClientId,
    string? ClientName,
    AgentActivityKind Kind,
    string? Origin,
    AgentActivityResult Result,
    DateTimeOffset StartedAt,
    int DurationMs,
    string? Detail);

/// <summary>Filtros del feed (todos opcionales).</summary>
public sealed record AgentActivityQueryDto(string? ClientId = null, AgentActivityKind? Kind = null, int Take = 100);

/// <summary>Lectura del feed de actividad de los agentes colmena (ADR-0045, Ola 3), tenant-scoped por el
/// filtro global. Solo lectura; la escritura la hace el servidor (IAgentActivityLog, Ola 2).</summary>
public interface IAgentActivityQuery
{
    Task<IReadOnlyList<AgentActivityDto>> ListAsync(AgentActivityQueryDto query, CancellationToken ct = default);
}

public sealed class AgentActivityQuery(IApplicationDbContext db) : IAgentActivityQuery
{
    public async Task<IReadOnlyList<AgentActivityDto>> ListAsync(AgentActivityQueryDto query, CancellationToken ct = default)
    {
        var take = Math.Clamp(query.Take, 1, 500);
        var q = db.AgentActivityLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.ClientId)) { q = q.Where(a => a.ClientId == query.ClientId); }
        if (query.Kind is { } k) { q = q.Where(a => a.Kind == k); }

        var rows = await q.OrderByDescending(a => a.StartedAt).Take(take).ToListAsync(ct);
        return rows.Select(a => new AgentActivityDto(
            a.Id, a.ClientId, a.ClientName, a.Kind, a.Origin, a.Result, a.StartedAt, a.DurationMs, a.Detail)).ToList();
    }
}
