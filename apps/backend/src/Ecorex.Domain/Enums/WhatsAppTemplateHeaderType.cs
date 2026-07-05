namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de encabezado (header) de una plantilla HSM de WhatsApp. En este corte solo se soporta
/// texto; imagen/documento/video quedan modelados para una fase posterior (deuda documentada,
/// ADR-0029).
/// </summary>
public enum WhatsAppTemplateHeaderType
{
    None,
    Text,
    Image,
    Document,
    Video
}
