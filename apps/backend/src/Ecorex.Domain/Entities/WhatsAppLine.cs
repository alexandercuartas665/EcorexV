using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Linea/instancia WhatsApp de un tenant (modulo 1.4). Entidad TENANT-SCOPED. La conexion
/// real (QR, sesion) se gestionara mediante el Evolution Connector en una fase posterior;
/// aqui se modela el ciclo de vida, el estado y la asignacion operativa a un asesor.
/// </summary>
public class WhatsAppLine : TenantEntity
{
    public string InstanceName { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public WhatsAppLineStatus Status { get; set; } = WhatsAppLineStatus.Created;
    public Guid? AssignedToTenantUserId { get; set; }
    public DateTimeOffset? LastConnectedAt { get; set; }
    public DateTimeOffset? LastStatusAt { get; set; }

    /// <summary>Proveedor de la linea (Evolution por QR o Meta Cloud API oficial). Default Evolution.</summary>
    public WhatsAppProvider Provider { get; set; } = WhatsAppProvider.Evolution;

    // ===== Credenciales para lineas Cloud (Meta WhatsApp Cloud API) =====
    /// <summary>phone_number_id de Meta: identifica el numero y enruta el webhook entrante.</summary>
    public string? CloudPhoneNumberId { get; set; }
    /// <summary>WABA id (WhatsApp Business Account) de Meta. Opcional, para referencia y plantillas.</summary>
    public string? CloudBusinessAccountId { get; set; }
    /// <summary>Token de acceso de Meta cifrado en reposo (ISecretProtector).</summary>
    public string? CloudAccessTokenEncrypted { get; set; }

    // ===== Credenciales para lineas YCloud (BSP oficial, api.ycloud.com) =====
    /// <summary>API key de YCloud cifrada en reposo (ISecretProtector). Unica credencial de auth.</summary>
    public string? YCloudApiKeyEncrypted { get; set; }
    /// <summary>Numero emisor registrado en YCloud, formato internacional sin "+". Enruta el webhook.</summary>
    public string? YCloudPhoneNumberId { get; set; }
    /// <summary>WABA id detectado/asociado en YCloud. Necesario para someter plantillas HSM.</summary>
    public string? YCloudWabaId { get; set; }
}
