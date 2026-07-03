namespace Ecorex.Domain.Enums;

/// <summary>Tipo de cuenta del tenant (Super Admin SaaS sec.5), separado del estado.</summary>
public enum TenantKind
{
    Standard,
    Demo,
    Internal,
    Test
}
