using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Ventana de horario de un <see cref="ContactWorkflowStep"/> (ADR-0056). Reemplaza
/// TAR_WORKFLOW_HORARIOS del legacy. Describe CUANDO puede dispararse el paso: rango de vigencia,
/// franja horaria, dias activos y parametros de ritmo (repetir/tamano de paquete). Las horas se
/// interpretan en la zona del tenant; el scheduler (Fase 2) compara contra UTC. TENANT-SCOPED.
/// </summary>
public class ContactWorkflowSchedule : TenantEntity
{
    public Guid ContactWorkflowStepId { get; set; }

    public ContactWorkflowStep? ContactWorkflowStep { get; set; }

    /// <summary>Rango de vigencia de la ventana (inclusive). Null = sin limite por ese extremo.</summary>
    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    /// <summary>Franja horaria (default 09:00-18:00), en la zona del tenant.</summary>
    public TimeOnly StartTime { get; set; } = new(9, 0);

    public TimeOnly EndTime { get; set; } = new(18, 0);

    /// <summary>Dias activos como CSV de dias ISO ("1,2,3,4,5" = lun..vie). Formato del legacy.</summary>
    public string ActiveDays { get; set; } = "1,2,3,4,5";

    /// <summary>Plantilla de mensaje (WhatsApp/email). Referencia suelta; el selector real es Fase 3.</summary>
    public string? TemplateId { get; set; }

    /// <summary>Cuenta/linea de mensajeria (WhatsAppLine / email_config). Referencia suelta; Fase 3.</summary>
    public Guid? AccountId { get; set; }

    /// <summary>Minutos entre disparos dentro de la ventana (rate). Null = sin repeticion intradia.</summary>
    public int? RepeatEvery { get; set; }

    /// <summary>Cuantos contactos por disparo (lote de rate limiting). Null = sin limite por lote.</summary>
    public int? PackageSize { get; set; }
}
