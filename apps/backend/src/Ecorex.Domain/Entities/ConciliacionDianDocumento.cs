using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Encabezado del documento mensual de Conciliacion DIAN de compras ("CCD"): uno por tenant y periodo
/// (anio/mes). Agrupa los renglones (<see cref="ConciliacionDianRenglon"/>) que se concilian ese mes.
/// Equivale a CONCILIACION_DIAN_COMPRAS del molde legacy, pero multi-tenant real (TenantId + filtro global)
/// en vez de la columna SUCURSAL a mano.
/// </summary>
public sealed class ConciliacionDianDocumento : TenantEntity
{
    /// <summary>Consecutivo legible del documento (p.ej. "CCD-0001"). Unico por tenant.</summary>
    public string Consecutivo { get; set; } = string.Empty;

    /// <summary>Anio del periodo conciliado.</summary>
    public int Anio { get; set; }

    /// <summary>Mes del periodo conciliado (1-12).</summary>
    public int Mes { get; set; }

    /// <summary>Estado del documento: ABIERTO (por defecto) / CERRADO.</summary>
    public string Estado { get; set; } = "ABIERTO";

    /// <summary>Renglones de conciliacion de este documento.</summary>
    public ICollection<ConciliacionDianRenglon> Renglones { get; set; } = new List<ConciliacionDianRenglon>();
}
