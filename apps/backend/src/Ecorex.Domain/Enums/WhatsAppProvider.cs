namespace Ecorex.Domain.Enums;

/// <summary>
/// Proveedor de una linea de WhatsApp:
/// - Evolution: API no oficial (Baileys), conexion por QR, instancia por linea.
/// - Cloud: API oficial de Meta (WhatsApp Cloud API), numero por phone_number_id + token, sin QR.
/// - Emulator: canal SIMULADO para pruebas. No se conecta a nada externo: los envios son no-op
///   exitosos (la respuesta queda en la conversacion y en la bitacora). Sirve para probar el
///   agente de punta a punta sin un numero real.
/// - YCloud: BSP oficial de WhatsApp (api.ycloud.com v2). API key por linea, sin QR; envio
///   saliente (texto/media por URL) y gestion de plantillas HSM sometidas a Meta.
/// El enum se PERSISTE como string (varchar(40)), asi que el orden de los valores no afecta la BD.
/// </summary>
public enum WhatsAppProvider
{
    Evolution,
    Cloud,
    Emulator,
    YCloud
}
