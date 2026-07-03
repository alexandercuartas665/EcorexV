using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Regla de automatizacion del tenant (modulo 2.5). Entidad TENANT-SCOPED. Define un disparador
/// (sin respuesta N horas / entra a etapa / lead nuevo) y una accion (crear seguimiento / alertar).
/// Se puede encender o apagar.
/// </summary>
public class AutomationRule : TenantEntity
{
    public string Name { get; set; } = null!;

    public AutomationTrigger Trigger { get; set; } = AutomationTrigger.NoReply;

    /// <summary>Umbral de minutos sin respuesta para el disparador NoReply.</summary>
    public int ThresholdMinutes { get; set; } = 30;

    /// <summary>Etapa objetivo para el disparador StageEntered.</summary>
    public Guid? StageId { get; set; }

    /// <summary>Ventana horaria (HH:mm) para el disparador ChatInTimeWindow.</summary>
    public string? TimeWindowStart { get; set; }
    public string? TimeWindowEnd { get; set; }

    public AutomationAction Action { get; set; } = AutomationAction.CreateFollowUp;

    /// <summary>Titulo de la tarea de seguimiento generada (accion CreateFollowUp).</summary>
    public string? FollowUpTitle { get; set; }

    /// <summary>Categoria de pregrabado para responder (accion CreateLeadAndReply).</summary>
    public string? TemplateCategory { get; set; }

    /// <summary>Nombre del turno destino (accion AssignToShift).</summary>
    public string? ShiftName { get; set; }

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Conteo de ejecuciones acumuladas (estadistica de la tarjeta).</summary>
    public int ExecutionCount { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }
}
