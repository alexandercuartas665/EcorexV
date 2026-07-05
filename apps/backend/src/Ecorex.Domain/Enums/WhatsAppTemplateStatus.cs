namespace Ecorex.Domain.Enums;

/// <summary>
/// Estado del ciclo de aprobacion de una plantilla HSM de WhatsApp ante Meta:
/// Draft -> Submitted -> Approved / Rejected (Paused/Disabled los reporta el proveedor). En
/// ECOREX no hay integracion real con Meta todavia (ADR-0029): Submit deja la plantilla en
/// Submitted como stub y SyncStatus es no-op.
/// </summary>
public enum WhatsAppTemplateStatus
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    Paused,
    Disabled
}
