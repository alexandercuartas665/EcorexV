using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Cancela el flujo EN MARCHA de una tarea cuando la tarea se CIERRA o ARCHIVA fuera de banda (el
/// usuario la cierra/archiva mientras el flujo sigue a mitad de camino). Sin esto, la instancia
/// quedaba Running con su paso vigente (IsCurrent + Pending) y TODO barrido -bandeja, tablero, motor
/// de agentes en nodos- la seguia tomando para siempre (flujo "zombi"): engrosaba cada consulta y
/// los agentes de seguimiento perseguian casos ya cerrados.
///
/// Marca la(s) instancia(s) Running de la tarea como Cancelled y sus pasos vigentes como
/// <see cref="WorkflowStepStatus.Skipped"/> + IsCurrent=false (la misma semantica de "rama pendiente
/// sin efecto" que usa el motor al completar una instancia), y limpia las marcas de espera del agente
/// para que el barrido no los considere ni por un reintento programado. Suelta ademas cualquier hold
/// de linea de WhatsApp que apuntara a esos pasos, para no dejar la linea muda apuntando a un paso que
/// ya no esta vigente (NO pone el candado de cierre-por-flujo: eso lo hace FlowLineRelease en los
/// caminos que lo requieren).
///
/// Idempotente (solo toca instancias Running). NO llama SaveChanges: corre sobre el MISMO DbContext
/// rastreado del que cierra/archiva la tarea, que guarda en su propia transaccion. Mismo patron que
/// <see cref="FlowLineRelease"/>.
/// </summary>
public static class WorkflowInstanceLifecycle
{
    /// <summary>Cancela el flujo en marcha de UNA tarea. Devuelve cuantas instancias se cancelaron.</summary>
    public static Task<int> CancelRunningForTaskAsync(
        IApplicationDbContext db, Guid taskItemId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => CancelRunningForTasksAsync(db, new[] { taskItemId }, now, cancellationToken);

    /// <summary>Cancela el flujo en marcha de VARIAS tareas (p.ej. el auto-archivado en lote). Devuelve
    /// el total de instancias canceladas.</summary>
    public static async Task<int> CancelRunningForTasksAsync(
        IApplicationDbContext db, IReadOnlyCollection<Guid> taskItemIds, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (taskItemIds is null || taskItemIds.Count == 0) { return 0; }
        var ids = taskItemIds.Distinct().ToList();

        // Solo instancias EN MARCHA ligadas a esas tareas (las que ya terminaron/cancelaron no se tocan:
        // idempotente). El flujo que completo la propia tarea ya quedo Completed, asi que esto solo atrapa
        // las que quedaron huerfanas al cerrar/archivar la tarea por fuera del motor.
        var instances = await db.WorkflowInstances
            .Where(i => i.TaskItemId != null && ids.Contains(i.TaskItemId!.Value)
                && i.Status == WorkflowInstanceStatus.Running)
            .ToListAsync(cancellationToken);
        if (instances.Count == 0) { return 0; }
        var instanceIds = instances.Select(i => i.Id).ToList();

        // Resuelve los pasos vigentes: dejan de ser corriente y quedan Skipped. Se limpian las marcas de
        // espera del agente para que WorkflowAgentStepWorker/Dispatcher no los recoja (ni por reintento).
        var steps = await db.WorkflowStepHistories
            .Where(s => instanceIds.Contains(s.InstanceId) && s.IsCurrent
                && s.Status == WorkflowStepStatus.Pending)
            .ToListAsync(cancellationToken);
        var skippedStepIds = new List<Guid>(steps.Count);
        foreach (var s in steps)
        {
            s.IsCurrent = false;
            s.Status = WorkflowStepStatus.Skipped;
            s.CompletedAt = now;
            s.AgentNextRetryAt = null;
            s.AgentDeadlineAt = null;
            s.AgentAttemptedAt ??= now;
            s.PendingWhatsAppConversationId = null;
            skippedStepIds.Add(s.Id);
        }

        // Libera cualquier hold de linea de WhatsApp que uno de esos pasos tuviera apartado (ADR-0122/0126):
        // sin esto la linea quedaria muda apuntando a un paso ya no vigente.
        if (skippedStepIds.Count > 0)
        {
            var heldConvs = await db.Conversations
                .Where(c => c.FlowHoldStepId != null && skippedStepIds.Contains(c.FlowHoldStepId!.Value))
                .ToListAsync(cancellationToken);
            foreach (var c in heldConvs)
            {
                c.FlowHoldStepId = null;
                c.FlowHoldNodeId = null;
            }
        }

        foreach (var i in instances)
        {
            i.Status = WorkflowInstanceStatus.Cancelled;
            i.CompletedAt = now;
        }
        return instances.Count;
    }
}
