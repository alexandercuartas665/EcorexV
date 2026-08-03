using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Registro de ejecucion de un paso de un <see cref="ContactWorkflow"/> sobre UN contacto en UNA ventana
/// (ADR-0056, Fase 2). Es la CLAVE DE IDEMPOTENCIA y dedupe del motor: antes de disparar, el dispatcher
/// verifica que no exista ya una fila para la tripleta (paso, ventana, contacto) en el dia; el indice
/// unico (tenant, paso, ventana, contacto, fecha) lo garantiza en BD aunque dos instancias del worker
/// corran a la vez. TENANT-SCOPED.
///
/// La "ventana" del dedupe = (ScheduleId + fecha local del tenant). Asi un contacto recibe el paso a lo
/// sumo UNA vez por dia por ventana, y una re-corrida en el mismo dia NO reenvia; al dia siguiente la
/// ventana vuelve a estar disponible (respeta ActiveDays/RepeatEvery del horario).
///
/// Las referencias a paso/ventana/contacto se guardan como Guid PLANOS (sin navegacion/FK dura hacia
/// ventana ni contacto) a proposito: los pasos y ventanas se REEMPLAZAN fisicamente al re-guardar el
/// disenador (Fase 1), y esta bitacora no debe bloquear ese reemplazo ni arrastrar cascadas multiples en
/// SQL Server. La UNICA FK dura es al paso, con cascada: si el paso se borra, su bitacora se va con el.
/// </summary>
public class ContactWorkflowRun : TenantEntity
{
    /// <summary>Paso ejecutado (FK dura, cascada).</summary>
    public Guid ContactWorkflowStepId { get; set; }

    public ContactWorkflowStep? ContactWorkflowStep { get; set; }

    /// <summary>Ventana de horario que disparo (referencia plana, sin FK: la ventana se reemplaza al re-guardar).</summary>
    public Guid ContactWorkflowScheduleId { get; set; }

    /// <summary>Contacto destino (referencia plana al Tercero, sin FK).</summary>
    public Guid TerceroId { get; set; }

    /// <summary>Fecha local del tenant en la que se disparo: parte de la clave de dedupe (una vez por dia).</summary>
    public DateOnly WindowDate { get; set; }

    /// <summary>Instante real del disparo (UTC).</summary>
    public DateTimeOffset DispatchedAtUtc { get; set; }

    public ContactWorkflowRunStatus Status { get; set; }

    /// <summary>Canal por el que se ejecuto: whatsapp / email / crm / conectar / redes.</summary>
    public string Channel { get; set; } = null!;

    /// <summary>Id externo del resultado (id del mensaje enviado, numero de la tarea CRM creada, etc.). Null si no aplica.</summary>
    public string? ExternalRef { get; set; }

    /// <summary>Motivo del fallo o del skip (sin secretos). Null si Sent.</summary>
    public string? Error { get; set; }
}
