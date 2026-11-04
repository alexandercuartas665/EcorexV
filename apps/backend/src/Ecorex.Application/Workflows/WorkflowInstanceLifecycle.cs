using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
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

        // Resuelve los pasos vigentes de esas instancias (dejan de ser corriente y quedan Skipped).
        await ResolveCurrentStepsForInstancesAsync(db, instanceIds, now, cancellationToken);

        foreach (var i in instances)
        {
            i.Status = WorkflowInstanceStatus.Cancelled;
            i.CompletedAt = now;
        }
        return instances.Count;
    }

    /// <summary>
    /// Resuelve los pasos vigentes (IsCurrent + Pending) de las instancias dadas: IsCurrent=false,
    /// Skipped, limpia esperas del agente y suelta el hold de WhatsApp. Lo usan el cierre en vivo
    /// (<see cref="CancelRunningForTasksAsync"/>) y el cancelar manual de una instancia
    /// (WorkflowDesignService.CancelInstanceAsync), para que una instancia cancelada NUNCA deje un paso
    /// vigente huerfano. NO llama SaveChanges. Devuelve cuantos pasos se resolvieron.
    /// </summary>
    public static async Task<int> ResolveCurrentStepsForInstancesAsync(
        IApplicationDbContext db, IReadOnlyCollection<Guid> instanceIds, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (instanceIds is null || instanceIds.Count == 0) { return 0; }
        var idList = instanceIds.Distinct().ToList();
        var steps = await db.WorkflowStepHistories
            .Where(s => idList.Contains(s.InstanceId) && s.IsCurrent
                && s.Status == WorkflowStepStatus.Pending)
            .ToListAsync(cancellationToken);
        await ResolveStepsAsync(db, steps, now, cancellationToken);
        return steps.Count;
    }

    /// <summary>
    /// Barre pasos HUERFANOS: pasos vigentes (IsCurrent + Pending) cuya instancia YA es terminal
    /// (Completed o Cancelled). Un frente vivo sobre una instancia terminada es una inconsistencia (p.ej.
    /// un cancelar-instancia viejo que no resolvio su paso) y todo barrido lo sigue tomando. NO toca
    /// instancias Stuck (estado defensivo heredado que puede esperar legitimamente). Acotado al tenant por
    /// el query filter del DbContext. NO llama SaveChanges. Devuelve cuantos pasos se resolvieron.
    /// </summary>
    public static async Task<int> ResolveOrphanCurrentStepsAsync(
        IApplicationDbContext db, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var steps = await db.WorkflowStepHistories
            .Where(s => s.IsCurrent && s.Status == WorkflowStepStatus.Pending
                && db.WorkflowInstances.Any(i => i.Id == s.InstanceId
                    && (i.Status == WorkflowInstanceStatus.Completed
                        || i.Status == WorkflowInstanceStatus.Cancelled)))
            .ToListAsync(cancellationToken);
        await ResolveStepsAsync(db, steps, now, cancellationToken);
        return steps.Count;
    }

    // Resolucion comun de una lista de pasos ya cargados (rastreados): los saca de vigentes + Skipped,
    // limpia las esperas del agente y suelta el hold de WhatsApp que apuntara a ellos.
    private static async Task ResolveStepsAsync(
        IApplicationDbContext db, List<WorkflowStepHistory> steps, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (steps.Count == 0) { return; }
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
        var heldConvs = await db.Conversations
            .Where(c => c.FlowHoldStepId != null && skippedStepIds.Contains(c.FlowHoldStepId!.Value))
            .ToListAsync(cancellationToken);
        foreach (var c in heldConvs)
        {
            c.FlowHoldStepId = null;
            c.FlowHoldNodeId = null;
        }
    }
}
