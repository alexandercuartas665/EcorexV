using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Renglon de la Conciliacion DIAN de compras: una factura electronica recibida (reportada a la DIAN por el
/// proveedor) que se concilia contra el documento interno del ERP. Entidad central del modulo. Equivale a
/// CONCILIACION_DIAN_COMPRAS_R del molde legacy, ahora multi-tenant real.
/// </summary>
public sealed class ConciliacionDianRenglon : TenantEntity
{
    /// <summary>Documento CCD (encabezado mensual) al que pertenece.</summary>
    public Guid DocumentoId { get; set; }
    public ConciliacionDianDocumento? Documento { get; set; }

    /// <summary>Estado / pestana del renglon.</summary>
    public EstadoConciliacionDian Estado { get; set; } = EstadoConciliacionDian.Conciliar;

    // ---- Datos de la factura DIAN (fuente: bot DIAN) ----
    /// <summary>CUFE: llave unica de la factura electronica en la DIAN.</summary>
    public string Cufe { get; set; } = string.Empty;
    public string TipoDocDian { get; set; } = string.Empty;
    public DateTimeOffset FechaEmision { get; set; }
    public string NombreProveedor { get; set; } = string.Empty;
    public string NitProveedor { get; set; } = string.Empty;
    public string NumFacturaProveedor { get; set; } = string.Empty;

    // ---- Cruce con el ERP ----
    /// <summary>Numero de factura/documento interno asignado por el cruce con el ERP. Vacio = sin cruce.
    /// Puede llevar la marca "Inconsistencia!" si cruzo con mas de un documento.</summary>
    public string NumFacturaSoldarco { get; set; } = string.Empty;

    /// <summary>Orden de compra interna (editable por el usuario en la grilla).</summary>
    public string OrdenCompraSoldarco { get; set; } = string.Empty;

    // ---- Montos ----
    public decimal SubtotalBruto { get; set; }
    public decimal DescuentoComercial { get; set; }
    public decimal SubtotalNeto { get; set; }
    public decimal IvaDescontable { get; set; }
    public decimal TotalAntesRetenciones { get; set; }
    public decimal RetRetefuente { get; set; }
    public decimal RetIca { get; set; }
    public decimal TotalFactura { get; set; }
    public string TipoPago { get; set; } = string.Empty;

    // ---- Estado de trabajo ----
    /// <summary>La factura fue aprobada (toggle). Auto = SI cuando cruzo limpio y nadie la habia tocado.</summary>
    public bool FacturaAprobada { get; set; }

    /// <summary>El usuario movio el toggle de aprobacion a mano. Si es true, el cruce NO pisa su decision
    /// (replica el "solo aprueba si FACTURA_APROBADA estaba vacio" del molde).</summary>
    public bool AprobadaManual { get; set; }

    /// <summary>El proveedor esta en la plataforma tecnologica (NEWTON) con representacion grafica; requisito
    /// para radicar eventos RADIAN. Se recalcula en cada cruce contra la fuente NEWTON.</summary>
    public bool PlataformaProveedor { get; set; }

    public string? RutEscaneado { get; set; }

    // ---- Eventos RADIAN (acuse 030, recibo 032, aceptacion 033, y 031/034) ----
    public bool Evento30 { get; set; }
    public bool Evento31 { get; set; }
    public bool Evento32 { get; set; }
    public bool Evento33 { get; set; }
    public bool Evento34 { get; set; }

    /// <summary>Marca de seleccion en la grilla (persiste entre paginado/refresh, como el molde).</summary>
    public bool Seleccionado { get; set; }
}
