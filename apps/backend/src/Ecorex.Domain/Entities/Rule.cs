using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Regla de negocio configurable (RulesEngine, FASE 4 ola 3). VerbName es la CLAVE del
/// registro tipado de verbos en DI (IRuleVerb.Name): el ejecutor resuelve la clase por
/// diccionario, nunca por reflexion sobre nombres del XML como el legacy (RCE). ParamsJson
/// guarda la configuracion del verbo segun su RuleVerbDescriptor (port tipado del protocolo
/// PARAM_XML). El modo Execute (SQL directo) del legacy esta PROHIBIDO. TENANT-SCOPED.
/// </summary>
public class Rule : TenantEntity
{
    public Guid DocumentId { get; set; }
    public RuleDocument? Document { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>Clave del verbo en el registro tipado (ej. PASAR_CAMPOS).</summary>
    public string VerbName { get; set; } = null!;

    public int SortOrder { get; set; }

    /// <summary>Parametros de configuracion del verbo (jsonb / nvarchar segun motor).</summary>
    public string? ParamsJson { get; set; }

    public RuleStatus Status { get; set; } = RuleStatus.Development;
}
