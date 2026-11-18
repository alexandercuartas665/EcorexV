using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Vinculo entre una tarea (TaskItem) y el identificador ESTABLE con que la conoce un sistema externo que la
/// origino (ADR-0176). Primer uso: el admin de anotaciones de la maqueta TRONOX, que manda cada anotacion con su
/// "idLocal" a POST /api/maqueta/solicitudes. Da la IDEMPOTENCIA: si llega otra vez el mismo
/// (Source, ExternalKey) se actualiza la tarea ya creada en vez de duplicarla. Guarda ademas los metadatos
/// tecnicos del origen (archivo real, handoff, selector...) como JSON. TENANT-SCOPED; unico por
/// (TenantId, Source, ExternalKey).
/// </summary>
public class ExternalTaskLink : TenantEntity
{
    /// <summary>Sistema de origen (ej. "maqueta-tronox"). Junto con ExternalKey forma la llave de idempotencia.</summary>
    public string Source { get; set; } = null!;

    /// <summary>Identificador estable del registro en el sistema de origen (ej. el idLocal de la anotacion).</summary>
    public string ExternalKey { get; set; } = null!;

    /// <summary>Tarea creada para este registro externo.</summary>
    public Guid TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    /// <summary>Metadatos del origen tal cual llegaron (JSON): pantalla, ruta, archivo, elemento, tema, viewport...</summary>
    public string? MetadataJson { get; set; }

    /// <summary>SHA-256 (hex) de la ultima captura adjuntada: un reenvio con la misma imagen no la vuelve a adjuntar.</summary>
    public string? LastCaptureSha256 { get; set; }

    /// <summary>Veces que se recibio este registro (1 = alta; mayor = reenvios que actualizaron la tarea).</summary>
    public int ReceivedCount { get; set; } = 1;
}
