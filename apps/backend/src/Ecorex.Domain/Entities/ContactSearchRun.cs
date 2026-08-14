using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Registro de UNA corrida de una busqueda de contactos (Cargador de contactos 000873). Existe para el
/// TOPE DIARIO POR FUENTE: como maximo N scrapes/dia por <see cref="Source"/> y tenant, para no penalizar
/// la cuenta en la red (LinkedIn/Facebook/Instagram bloquean por exceso). El conteo se hace por
/// <see cref="RunAt"/> &gt;= inicio del dia UTC. TENANT-SCOPED (filtro global por reflexion).
/// </summary>
public class ContactSearchRun : TenantEntity
{
    /// <summary>Definicion que se ejecuto (<see cref="ContactSearchDefinition"/>).</summary>
    public Guid DefinitionId { get; set; }

    /// <summary>Fuente de la busqueda (Maps/LinkedIn/Facebook/Instagram/Web/X), como texto.</summary>
    public string Source { get; set; } = null!;

    /// <summary>Momento de la corrida (UTC). Base del conteo diario por fuente.</summary>
    public DateTimeOffset RunAt { get; set; }

    /// <summary>La corrida termino OK.</summary>
    public bool Ok { get; set; }

    /// <summary>Contactos insertados en esa corrida.</summary>
    public int Inserted { get; set; }
}
