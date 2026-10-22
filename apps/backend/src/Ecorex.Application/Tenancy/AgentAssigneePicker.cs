using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// ADR-0128: reparto de asignaciones del agente. Elige el SIGUIENTE usuario para las tareas/actividades que el
/// agente crea (crear_tarea / crear_actividad):
///  1) Si el AGENTE tiene una lista ORDENADA de TenantUser ids (AiAgent.AssigneeUserIdsJson), ROUND-ROBIN sobre
///     esa lista rotando con AiAgent.AssigneeCursor (una ronda por registro: A, B, A, B...). Salta ids que ya no
///     sean un usuario activo del tenant; si ninguno sirve, cae al pool.
///  2) FALLBACK (sin lista o sin agente en contexto): pool de asesores del tenant marcados AssignableByAgent,
///     menos-recientemente-asignado (comportamiento anterior de crear_tarea).
/// Devuelve (null, null) si no hay a quien asignar. Persiste el cursor / la marca de ultima asignacion.
/// Corre sobre el mismo IApplicationDbContext del toolset (mismo scope de tenant).
/// </summary>
public static class AgentAssigneePicker
{
    public static async Task<(Guid? UserId, string? Name)> PickAsync(
        IApplicationDbContext db, Guid? agentId, CancellationToken ct)
    {
        // 1) Lista configurada en el agente -> round-robin por cursor.
        if (agentId is Guid aid)
        {
            var agent = await db.AiAgents.FirstOrDefaultAsync(a => a.Id == aid, ct);
            if (agent is not null)
            {
                var ids = ParseIds(agent.AssigneeUserIdsJson);
                if (ids.Count > 0)
                {
                    var start = ((agent.AssigneeCursor % ids.Count) + ids.Count) % ids.Count;
                    for (var i = 0; i < ids.Count; i++)
                    {
                        var idx = (start + i) % ids.Count;
                        var candidate = ids[idx];
                        var name = await db.TenantUsers.AsNoTracking()
                            .Where(u => u.Id == candidate && u.Status == PlatformUserStatus.Active)
                            .Select(u => u.Email)
                            .FirstOrDefaultAsync(ct);
                        if (name is not null)
                        {
                            agent.AssigneeCursor = (idx + 1) % ids.Count;  // el proximo arranca despues del elegido
                            await db.SaveChangesAsync(ct);
                            return (candidate, name);
                        }
                    }
                    // Ningun id de la lista es un usuario activo valido -> cae al pool.
                }
            }
        }

        // 2) Fallback: pool de asesores del tenant marcados AssignableByAgent (menos-recientemente-asignado).
        var next = await db.Asesores
            .Where(a => a.IsActive && a.AssignableByAgent && a.TenantUserId != null)
            .OrderBy(a => a.LastAgentAssignmentAt == null ? 0 : 1)
            .ThenBy(a => a.LastAgentAssignmentAt)
            .ThenBy(a => a.Nombre)
            .FirstOrDefaultAsync(ct);
        if (next?.TenantUserId is not Guid uid) { return (null, null); }
        next.LastAgentAssignmentAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return (uid, next.Nombre);
    }

    private static List<Guid> ParseIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new(); }
        try
        {
            var arr = JsonSerializer.Deserialize<List<Guid>>(json);
            return arr?.Where(g => g != Guid.Empty).ToList() ?? new();
        }
        catch { return new(); }
    }
}
