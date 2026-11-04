using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Limpieza PUNTUAL del backlog de flujos "zombi": instancias <see cref="WorkflowInstanceStatus.Running"/>
/// cuya tarea ya esta cerrada (Done/Closed) o archivada. El fix de ADR-0150 evita que se sigan creando de
/// aqui en adelante, pero las que ya existen -sobre todo las de tareas YA archivadas- no vuelven a pasar
/// por ningun hook de cierre, asi que necesitan una pasada unica.
///
/// Reusa <see cref="WorkflowInstanceLifecycle"/> (misma semantica exacta que el cierre en vivo) y se corre
/// ACOTADA a cada tenant (el llamador fija el contexto de tenant; el query filter aisla). Soporta DRY-RUN:
/// con apply=false solo cuenta, sin tocar nada. Idempotente: una segunda corrida no encuentra nada.
/// </summary>
public interface IZombieFlowCleanupService
{
    /// <summary>Tenants con al menos una tarea (para iterar el barrido). El llamador fija el contexto por tenant.</summary>
    Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancela (o cuenta, si apply=false) las instancias Running de tareas cerradas/archivadas del tenant
    /// actual. Devuelve cuantas instancias se cancelarian/cancelaron.
    /// </summary>
    Task<int> CleanupForTenantAsync(DateTimeOffset nowUtc, bool apply, CancellationToken cancellationToken = default);
}

public sealed class ZombieFlowCleanupService : IZombieFlowCleanupService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public ZombieFlowCleanupService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken = default)
        => await _db.Tenants.IgnoreQueryFilters()
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

    public async Task<int> CleanupForTenantAsync(
        DateTimeOffset nowUtc, bool apply, CancellationToken cancellationToken = default)
    {
        // Tareas del tenant que estan cerradas/archivadas y AUN tienen una instancia Running (zombi).
        var zombieTaskIds = await (
            from i in _db.WorkflowInstances
            where i.Status == WorkflowInstanceStatus.Running && i.TaskItemId != null
            join t in _db.TaskItems on i.TaskItemId!.Value equals t.Id
            where t.IsArchived || t.Status == TaskItemStatus.Done || t.Status == TaskItemStatus.Closed
            select t.Id)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (zombieTaskIds.Count == 0) { return 0; }

        if (!apply)
        {
            // DRY-RUN: cuenta cuantas instancias Running se cancelarian, sin tocar nada.
            return await _db.WorkflowInstances.CountAsync(
                i => i.Status == WorkflowInstanceStatus.Running && i.TaskItemId != null
                    && zombieTaskIds.Contains(i.TaskItemId!.Value),
                cancellationToken);
        }

        var cancelled = await WorkflowInstanceLifecycle
            .CancelRunningForTasksAsync(_db, zombieTaskIds, nowUtc, cancellationToken);

        // Rastro de auditoria por tarea afectada (el helper no escribe actividad; lo hace el llamador).
        var tenantId = _tenant.TenantId ?? Guid.Empty;
        foreach (var taskId in zombieTaskIds)
        {
            _db.TaskItemActivities.Add(new TaskItemActivity
            {
                TenantId = tenantId,
                TaskItemId = taskId,
                Type = TaskActivityType.Action,
                ActorUserId = null,
                ActorName = "Sistema",
                Text = "cancelo el flujo zombi en la limpieza de mantenimiento del backlog (ADR-0150)"
            });
        }

        // Un solo SaveChanges = una transaccion atomica para todo el tenant (regla de multi-tabla).
        await _db.SaveChangesAsync(cancellationToken);
        return cancelled;
    }
}
