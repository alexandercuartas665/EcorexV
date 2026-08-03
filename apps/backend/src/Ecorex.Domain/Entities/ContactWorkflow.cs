using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Disenador de acciones atado a un filtro guardado de contactos (ADR-0056, port del
/// ucWorkflowDesigner legacy). Es una LISTA SECUENCIAL de pasos (no un grafo BPMN) que, en Fase 2,
/// se ejecutara sobre el segmento de contactos que define su <see cref="TerceroFiltro"/>. Vinculo
/// FORMAL 1:1 con el filtro (lo que faltaba en el legacy, que ataba por convencion de string).
/// TENANT-SCOPED. Concurrencia optimista portable (Version, ADR-0013).
/// </summary>
public class ContactWorkflow : TenantEntity, IVersioned
{
    /// <summary>Filtro guardado al que pertenece este disenador de acciones (1:1, indice unico).</summary>
    public Guid TerceroFiltroId { get; set; }

    public TerceroFiltro? TerceroFiltro { get; set; }

    /// <summary>Nombre del workflow. Por defecto "Acciones de &lt;filtro&gt;".</summary>
    public string Nombre { get; set; } = null!;

    /// <summary>On/off del workflow completo. La EJECUCION real es Fase 2.</summary>
    public bool Activo { get; set; } = true;

    /// <summary>Token de concurrencia optimista portable (lo incrementa el interceptor).</summary>
    public long Version { get; set; }

    /// <summary>Pasos secuenciales (0..n). El orden lo lleva <see cref="ContactWorkflowStep.Orden"/>.</summary>
    public List<ContactWorkflowStep> Steps { get; set; } = new();
}
