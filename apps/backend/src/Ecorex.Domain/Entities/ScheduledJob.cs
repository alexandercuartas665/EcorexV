using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Cabecera de una programacion del Motor de programaciones (modulo 000889 "Programar actividad").
/// Es un "cron de negocio" gobernado y TENANT-SCOPED: al dispararse, envia una NOTIFICACION por
/// sus canales o CREA una ACTIVIDAD consumiendo el concepto (categoria/subcategoria) elegido.
/// Lleva N reglas de recurrencia (<see cref="Rules"/>) y N canales (<see cref="Channels"/>).
/// Concurrencia optimista portable (Version, ADR-0013). Sucesor de PROG_ACTIVIDADES (origen VB).
/// </summary>
public class ScheduledJob : TenantEntity, IVersioned
{
    /// <summary>Consecutivo legible por tenant (origen "PAC", ej. "PAC-000001").</summary>
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    /// <summary>Notificacion (envia alerta) o Actividad (crea una tarea del concepto).</summary>
    public ScheduledJobType Type { get; set; } = ScheduledJobType.Notification;

    public ScheduledJobStatus Status { get; set; } = ScheduledJobStatus.Active;

    /// <summary>Prioridad del origen; sin UI en el prototipo todavia.</summary>
    public ScheduledJobPriority Priority { get; set; } = ScheduledJobPriority.Normal;

    /// <summary>Area/Sede (Entidad) del origen. Referencia suelta (sin FK dura en P1); sin UI aun.</summary>
    public Guid? AreaEntityId { get; set; }

    /// <summary>Solo Type=Activity: categoria del concepto que se disparara. Referencia suelta en P1.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Solo Type=Activity: subcategoria (concepto) que se disparara. Referencia suelta en P1.</summary>
    public Guid? SubcategoryId { get; set; }

    /// <summary>
    /// Encargado OPCIONAL de la programacion (TenantUser). Type=Activity: se pasa como
    /// <c>CreateTaskItemRequest.AssigneeTenantUserId</c> al crear la tarea (si va null, la
    /// actividad nace Pending / sin asignar, que es el comportamiento por defecto de TaskItemService).
    /// Type=Notification: destinatario in-app del aviso. Referencia suelta (sin FK dura).
    /// </summary>
    public Guid? AssigneeTenantUserId { get; set; }

    /// <summary>Token de concurrencia optimista portable (lo incrementa el interceptor).</summary>
    public long Version { get; set; }

    /// <summary>Reglas de recurrencia (1:N). El prototipo las llama "conjuntos de reglas de ejecucion".</summary>
    public List<ScheduledJobRule> Rules { get; set; } = new();

    /// <summary>Canales de transmision (N).</summary>
    public List<ScheduledJobChannel> Channels { get; set; } = new();
}
