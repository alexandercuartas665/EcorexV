using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Plantilla HTML para imprimir cotizaciones al cliente (modulo Plantillas). Entidad TENANT-SCOPED.
/// Cada agencia puede tener varias plantillas con nombre; una marcada como predeterminada. El HTML
/// admite marcadores (ej. {{contactName}}, {{destination}}, {{field.fieldKey}}) que se reemplazan
/// con los datos del lead al generar el PDF.
/// </summary>
public class QuoteTemplate : TenantEntity
{
    /// <summary>Nombre visible de la plantilla (ej. "Cotizacion playa").</summary>
    public string Name { get; set; } = null!;

    /// <summary>Contenido HTML de la plantilla, con marcadores {{...}}.</summary>
    public string HtmlContent { get; set; } = "";

    /// <summary>Plantilla predeterminada de la agencia para imprimir cotizaciones.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Si es true, al enviar la cotizacion se manda como imagen (PNG); si es false, como PDF adjunto.</summary>
    public bool SendAsImage { get; set; }
}
