namespace CubotNails.Domain.Enums;

/// <summary>
/// Proveedor de una linea de WhatsApp:
/// - Evolution: API no oficial (Baileys), conexion por QR, instancia por linea.
/// - Cloud: API oficial de Meta (WhatsApp Cloud API), numero por phone_number_id + token, sin QR.
/// </summary>
public enum WhatsAppProvider
{
    Evolution,
    Cloud
}
