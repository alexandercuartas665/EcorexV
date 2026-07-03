using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Rules;

/// <summary>
/// Maquina de estados del nucleo TaskItem (ADR-0013). Fuente unica de verdad de las
/// transiciones validas; los servicios de Application la consultan antes de cambiar estado.
/// Closed es terminal: la tarea queda de solo lectura.
/// FASE 4: cuando el ActivityType tenga WorkflowDefinition, el flujo definira las
/// transiciones y esta tabla libre dejara de aplicar para esos tipos.
/// </summary>
public static class TaskItemStateMachine
{
    private static readonly IReadOnlyDictionary<TaskItemStatus, TaskItemStatus[]> Transitions =
        new Dictionary<TaskItemStatus, TaskItemStatus[]>
        {
            [TaskItemStatus.Pending] = [TaskItemStatus.Active, TaskItemStatus.InProgress, TaskItemStatus.Suspended],
            [TaskItemStatus.Active] = [TaskItemStatus.InProgress, TaskItemStatus.Suspended, TaskItemStatus.Done],
            [TaskItemStatus.InProgress] = [TaskItemStatus.Done, TaskItemStatus.Suspended],
            [TaskItemStatus.Suspended] = [TaskItemStatus.InProgress, TaskItemStatus.Active],
            // Done -> InProgress es la reapertura; Done -> Closed cierra definitivamente.
            [TaskItemStatus.Done] = [TaskItemStatus.Closed, TaskItemStatus.InProgress],
            // Closed es terminal: no admite ninguna transicion (solo lectura).
            [TaskItemStatus.Closed] = []
        };

    /// <summary>Indica si la transicion from -> to esta permitida.</summary>
    public static bool CanTransition(TaskItemStatus from, TaskItemStatus to)
        => from != to && Transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>Estados destino validos desde el estado dado (vacio para Closed).</summary>
    public static IReadOnlyList<TaskItemStatus> AllowedTargets(TaskItemStatus from)
        => Transitions.TryGetValue(from, out var targets) ? targets : [];

    /// <summary>Un estado terminal no admite mas cambios (la tarea es de solo lectura).</summary>
    public static bool IsTerminal(TaskItemStatus status) => AllowedTargets(status).Count == 0;
}
