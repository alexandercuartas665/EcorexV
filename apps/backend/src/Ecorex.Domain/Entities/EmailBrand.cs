using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// MARCA DE CORREO del tenant (ADR-0177): logo, colores, estilo por defecto y datos del pie que el agente
/// editor de plantillas aplica a TODOS los correos. Una fila por tenant (indice unico). Entidad TENANT-SCOPED.
/// Si no hay fila, el agente cae al logo del tenant (Tenant.LogoUrl) y elige una paleta sobria.
/// </summary>
public class EmailBrand : TenantEntity
{
    /// <summary>Logo especifico para correos (p.ej. version blanca para la banda oscura). Ruta servible
    /// (/i/... de la galeria) o URL https. Null = usar el logo de la empresa (Tenant.LogoUrl).</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Color principal de marca (#RRGGBB): banda del encabezado, titulos.</summary>
    public string? ColorPrimario { get; set; }

    /// <summary>Color de acento (#RRGGBB): botones (CTA), detalles.</summary>
    public string? ColorAcento { get; set; }

    /// <summary>Clave del estilo de diseno por defecto (ver EmailDesignStyles). Null = "corporativo".</summary>
    public string? EstiloPorDefecto { get; set; }

    /// <summary>Sitio web de la empresa (https://...) para el pie y los botones por defecto.</summary>
    public string? SitioWeb { get; set; }

    /// <summary>Texto adicional del pie (redes, horario, aviso legal corto). Texto plano.</summary>
    public string? PieTexto { get; set; }
}
