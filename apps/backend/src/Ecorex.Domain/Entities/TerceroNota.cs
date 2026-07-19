using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Nota / gestion de "Contacto cliente" de un Tercero del Directorio General (000232).
/// Bitacora tipo timeline: cada registro es una gestion realizada con el tercero
/// (nota, oportunidad, solicitud, actividad, PQR o proxima atencion), clasificada por
/// categoria y sub-categoria libres. Multi-tenant (filtro global por reflexion).
/// </summary>
public class TerceroNota : TenantEntity
{
    /// <summary>El tercero (empresa o persona) al que pertenece la nota.</summary>
    public Guid TerceroId { get; set; }
    public Tercero? Tercero { get; set; }

    /// <summary>Texto de la gestion.</summary>
    public string Texto { get; set; } = null!;

    /// <summary>Tipo de accion: Nota, Oportunidad, Solicitud, Actividad, PQR, Atencion.</summary>
    public string Accion { get; set; } = "Nota";

    /// <summary>Categoria libre de la gestion (ej. Comercial, Seguimiento, Soporte).</summary>
    public string? Categoria { get; set; }

    /// <summary>Sub-categoria libre (ej. Llamada, Correo, Visita).</summary>
    public string? Subcategoria { get; set; }

    /// <summary>Nombre del autor que registro la gestion (texto libre).</summary>
    public string? Autor { get; set; }

    /// <summary>
    /// Concepto de actividad del CRM (000125) que origino esta gestion. Null = gestion libre (nota
    /// rapida sin concepto). FK NO ACTION (archivar el concepto no toca la bitacora).
    /// </summary>
    public Guid? ConceptoActividadId { get; set; }
    public ConceptoActividad? ConceptoActividad { get; set; }

    /// <summary>
    /// Respuesta del formulario capturado para esta gestion (cuando el concepto tiene formulario).
    /// Null = gestion sin formulario. FK NO ACTION.
    /// </summary>
    public Guid? FormResponseId { get; set; }
    public FormResponse? FormResponse { get; set; }

    /// <summary>Valor de la gestion (cuando el concepto maneja valor, ej. una oportunidad). Null = sin valor.</summary>
    public decimal? Valor { get; set; }
}
