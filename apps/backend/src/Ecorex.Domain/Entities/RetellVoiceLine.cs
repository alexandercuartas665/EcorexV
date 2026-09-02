using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Una LINEA de voz IA (Retell sobre Telnyx) del tenant. Un tenant puede tener VARIAS lineas (cada una con
/// su nombre, numero saliente y credenciales), igual que <see cref="WhatsAppLine"/>. Secretos SOLO cifrados
/// (columnas *_encrypted con ISecretProtector = DataProtection); nunca se loguean ni se exponen en DTOs.
/// El repo es publico: prohibido versionar secretos.
/// </summary>
public class RetellVoiceLine : TenantEntity
{
    /// <summary>Nombre legible de la linea (ej. "Ventas", "Cobranzas").</summary>
    public string Name { get; set; } = null!;

    /// <summary>API key de Retell de la linea (cifrada).</summary>
    public string? RetellApiKeyEncrypted { get; set; }

    /// <summary>Numero saliente propio en formato E.164 (importado en Retell).</summary>
    public string? FromNumber { get; set; }

    // ---- Telnyx (Elastic SIP Trunking) ----

    /// <summary>FQDN de terminacion SIP de Telnyx (ej. sip.telnyx.com), usado al importar el numero.</summary>
    public string? TerminationUri { get; set; }

    /// <summary>Usuario SIP del trunk de Telnyx (va tambien en el header X-Telnyx-Username en salientes).</summary>
    public string? SipUsername { get; set; }

    /// <summary>Password SIP del trunk de Telnyx (cifrado).</summary>
    public string? SipPasswordEncrypted { get; set; }

    // ---- Agente de voz Retell provisionado ----

    /// <summary>Voz de Retell a usar (voice_id). Se elige una voz en espanol.</summary>
    public string? VoiceId { get; set; }

    /// <summary>Idioma/locale del agente (ej. "es-419" para espanol LatAm).</summary>
    public string Language { get; set; } = "es-419";

    /// <summary>Habilita el uso de esta linea para llamadas IA.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Linea por defecto del tenant: la que usa el motor de acciones cuando no se especifica otra.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Ultima validacion exitosa de la key/numero contra Retell.</summary>
    public DateTimeOffset? LastValidatedAt { get; set; }
}
