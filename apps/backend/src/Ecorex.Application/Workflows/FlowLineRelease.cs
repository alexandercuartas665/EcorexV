using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Workflows;

/// <summary>
/// ADR-0126: libera la LINEA de WhatsApp que un flujo habia APARTADO (hold ADR-0122) cuando el caso deja de
/// necesitarla. Un nodo-agente toma la linea (Conversation.FlowHoldStepId) para preguntarle al cliente; esa marca
/// solo la soltaba el propio paso al resolver/fallar dentro de WorkflowAgentStepRunner. Si la tarea se CIERRA (a
/// mano en el tablero o porque el flujo termina) o un paso retenido se APAGA por el motor (reabrir, barrido de
/// cierre), la marca quedaba colgada: SARA muda, ninguna otra tarea podia tomar la linea y los entrantes del
/// cliente reactivaban un paso de una tarea ya cerrada. Estos helpers son el punto unico de liberacion para esos
/// caminos. NO llaman SaveChanges: corren sobre el mismo DbContext rastreado del que cierra/avanza, que guarda.
/// </summary>
public static class FlowLineRelease
{
    /// <summary>
    /// La tarea se esta CERRANDO (Done/Closed), a mano o por el flujo. Para cada conversacion que un paso de esta
    /// tarea tenia apartada: suelta el hold (FlowHoldStepId/NodeId), deja la marca de cierre-por-flujo
    /// (FlowHandledCloseAt/TaskId) para que el candado de SARA no recree un caso/tarea (AgentTaskIdempotency), y
    /// ABANDONA el paso-agente retenido que siga vigente (IsCurrent=false, Pending->Skipped, limpia la espera de
    /// WhatsApp y el reintento) para que el barrido y la ingesta dejen de apuntar a una tarea cerrada. La linea
    /// queda libre: el proximo entrante del cliente lo atiende SARA (agente conversacional de la linea).
    /// </summary>
    public static async Task OnTaskClosedAsync(
        IApplicationDbContext db, Guid tenantId, Guid taskItemId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var instanceIds = await db.WorkflowInstances
            .Where(i => i.TenantId == tenantId && i.TaskItemId == taskItemId)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
        if (instanceIds.Count == 0) { return; }

        var stepIds = await db.WorkflowStepHistories
            .Where(s => instanceIds.Contains(s.InstanceId))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        if (stepIds.Count == 0) { return; }

        var heldConvs = await db.Conversations
            .Where(c => c.FlowHoldStepId != null && stepIds.Contains(c.FlowHoldStepId!.Value))
            .ToListAsync(cancellationToken);
        if (heldConvs.Count == 0) { return; }

        var releasedStepIds = new HashSet<Guid>();
        foreach (var conv in heldConvs)
        {
            if (conv.FlowHoldStepId is Guid sid) { releasedStepIds.Add(sid); }
            conv.FlowHoldStepId = null;
            conv.FlowHoldNodeId = null;
            // Marca de cierre-por-flujo (la lee el candado de SARA, ventana en AgentTaskIdempotency): el caso ya se
            // cerro, SARA puede conversar pero NO debe crear tarea/lead duplicados.
            conv.FlowHandledCloseAt = now;
            conv.FlowHandledTaskId = taskItemId;
        }

        // Abandona el/los paso(s) retenido(s) que sigan vigentes (cierre MANUAL: el motor no los apago). Para el
        // cierre por flujo ya estan no-vigentes y esto es no-op.
        var liveHeld = await db.WorkflowStepHistories
            .Where(s => releasedStepIds.Contains(s.Id) && s.IsCurrent)
            .ToListAsync(cancellationToken);
        foreach (var s in liveHeld)
        {
            s.IsCurrent = false;
            if (s.Status == WorkflowStepStatus.Pending) { s.Status = WorkflowStepStatus.Skipped; }
            s.PendingWhatsAppConversationId = null;
            s.AgentNextRetryAt = null;
            s.AgentAttemptedAt ??= now;
        }

        db.TaskItemActivities.Add(new TaskItemActivity
        {
            TenantId = tenantId,
            TaskItemId = taskItemId,
            Type = TaskActivityType.Action,
            ActorName = "Sistema (flujo)",
            Text = "libero la linea de WhatsApp al cerrar la tarea (el flujo ya no la atiende)"
        });
    }

    /// <summary>
    /// El motor APAGA (IsCurrent=false) un conjunto de pasos SIN pasar por el runner (reabrir aguas abajo, barrido
    /// de cierre de instancia). Si alguno tenia la linea apartada, suelta el hold para no dejarla colgada. No pone
    /// marca de cierre (no es necesariamente un cierre de caso) ni toca el paso (el llamador ya lo apaga).
    /// </summary>
    public static async Task ReleaseHoldsForStepsAsync(
        IApplicationDbContext db, IReadOnlyCollection<Guid> stepIds, CancellationToken cancellationToken)
    {
        if (stepIds.Count == 0) { return; }
        var convs = await db.Conversations
            .Where(c => c.FlowHoldStepId != null && stepIds.Contains(c.FlowHoldStepId!.Value))
            .ToListAsync(cancellationToken);
        foreach (var conv in convs)
        {
            conv.FlowHoldStepId = null;
            conv.FlowHoldNodeId = null;
        }
    }
}
