using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Historial APPEND-ONLY de ejecuciones de reglas (RulesEngine, FASE 4 ola 3). El legacy
/// perdia el historial (tabla inexistente); aqui el motor SIEMPRE registra cada ejecucion
/// (exito o fallo) con TTL de 90 dias (ExpiresAt). El worker de limpieza borra fisicamente
/// solo filas expiradas: es el UNICO DELETE fisico permitido (log con TTL documentado,
/// ADR-0016). RuleNameSnapshot conserva el nombre aunque la regla cambie. TENANT-SCOPED.
/// </summary>
public class RuleExecutionLog : TenantEntity
{
    public Guid RuleId { get; set; }
    public Rule? Rule { get; set; }

    public Guid? ExecutedByTenantUserId { get; set; }

    /// <summary>Nombre de la regla al momento de ejecutar (sobrevive a renombres).</summary>
    public string RuleNameSnapshot { get; set; } = null!;

    public RuleTriggerKind TriggerKind { get; set; }

    /// <summary>Payload de invocacion (contexto): jsonb / nvarchar segun motor.</summary>
    public string? ContextJson { get; set; }

    public RuleExecutionStatus Status { get; set; }

    public int RecordsAffected { get; set; }

    public int DurationMs { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Vencimiento del TTL (90 dias desde la ejecucion).</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
