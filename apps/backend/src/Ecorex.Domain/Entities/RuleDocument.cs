using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Documento de configuracion de reglas (RulesEngine, FASE 4 ola 3: port de
/// cl_gestion_reglas / modulo legacy 000802). Agrupa reglas por categoria funcional
/// (FORMULARIOS, PROCESOS, IA...). DocumentCode es unico por tenant. Nunca se borra
/// fisicamente: se archiva (IsArchived). TENANT-SCOPED.
/// </summary>
public class RuleDocument : TenantEntity
{
    /// <summary>Codigo legible del documento (ej. RUL-005). Unico por tenant.</summary>
    public string DocumentCode { get; set; } = null!;

    public string Name { get; set; } = null!;

    /// <summary>Categoria funcional (ej. FORMULARIOS / PROCESOS / IA).</summary>
    public string Category { get; set; } = null!;

    public string? Description { get; set; }

    public RuleStatus Status { get; set; } = RuleStatus.Development;

    public bool IsArchived { get; set; }
}
