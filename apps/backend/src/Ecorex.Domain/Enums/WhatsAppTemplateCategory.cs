namespace Ecorex.Domain.Enums;

/// <summary>
/// Categoria de Meta para una plantilla HSM de WhatsApp. Determina las reglas de revision y el
/// tipo de contenido permitido.
/// - Marketing: promociones, ofertas, novedades (requiere opt-in).
/// - Utility: notificaciones transaccionales de un evento (confirmaciones, recordatorios).
/// - Authentication: codigos de un solo uso (OTP). Requiere componentes especiales de Meta.
/// </summary>
public enum WhatsAppTemplateCategory
{
    Marketing,
    Utility,
    Authentication
}
