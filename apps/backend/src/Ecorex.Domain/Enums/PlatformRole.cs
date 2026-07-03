namespace Ecorex.Domain.Enums;

/// <summary>Rol global del operador del SaaS (Super Admin SaaS sec.13). No se mezcla con roles de tenant.</summary>
public enum PlatformRole
{
    SuperAdmin,
    FinanceOperator,
    SupportOperator,
    TechnicalOperator,
    Auditor,
    Analyst
}
