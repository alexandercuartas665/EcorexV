using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Bitacora de ejecucion del worker (la pieza que el origen dejo como placeholder: ContarEjecutadosHoy/
/// ContarErrores retornaban 0). Cada disparo del motor de programaciones escribe una fila con su
/// resultado, lo que habilita KPIs ("ejecutados hoy" / "errores") y la IDEMPOTENCIA (una ventana =
/// un disparo). Se llena en P2 (worker); en P1 solo existe la tabla. Tenant-scoped.
/// </summary>
public class ScheduledJobRun : TenantEntity
{
    public Guid JobId { get; set; }
    public ScheduledJob Job { get; set; } = null!;

    /// <summary>Regla que disparo (null si el disparo no se atribuye a una regla concreta).</summary>
    public Guid? RuleId { get; set; }

    /// <summary>Instante real del disparo.</summary>
    public DateTimeOffset FiredAt { get; set; }

    public ScheduledJobRunResult Result { get; set; }

    /// <summary>Detalle libre (mensaje de error, motivo del skip, etc.).</summary>
    public string? Detail { get; set; }

    /// <summary>Referencia de la entidad creada al disparar (ej. numero de la TaskItem para Type=Activity).</summary>
    public string? CreatedEntityRef { get; set; }
}
