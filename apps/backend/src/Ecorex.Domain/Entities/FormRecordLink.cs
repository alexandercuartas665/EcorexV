using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Enlace maestro-detalle entre registros de formulario (Formularios avanzados, ola F5, doc 01 D7):
/// un campo Subform del formulario padre agrupa N registros HIJOS (respuestas de otra definicion).
/// A diferencia del GridDetail (filas embebidas en el jsonb del padre), cada hijo es un
/// <see cref="FormResponse"/> propio, reportable/consultable aparte. TENANT-SCOPED.
/// </summary>
public class FormRecordLink : TenantEntity
{
    /// <summary>Registro padre (la respuesta que contiene el campo Subform).</summary>
    public Guid ParentResponseId { get; set; }
    public FormResponse? ParentResponse { get; set; }

    /// <summary>FieldCode del campo Subform en el padre (un padre puede tener varios subformularios).</summary>
    public string ParentFieldCode { get; set; } = null!;

    /// <summary>
    /// Identidad ESTABLE de la FILA del GridDetail a la que cuelga el hijo (ADR-0085). Null = enlace de
    /// Subform clasico (a nivel del campo, no de una fila). Con valor = "gestion" por fila: el hijo (una
    /// gestion: cotizacion, oportunidad, PQR...) pertenece a UNA fila/persona del grid del padre. El id se
    /// guarda en la propia fila del jsonb del padre (clave <c>__rowId</c>), asi el vinculo sobrevive a
    /// reordenar/insertar/borrar filas. Longitud acotada (GUID en texto).
    /// </summary>
    public string? ParentRowId { get; set; }

    /// <summary>Registro hijo (respuesta de la definicion hija).</summary>
    public Guid ChildResponseId { get; set; }
    public FormResponse? ChildResponse { get; set; }

    public int SortOrder { get; set; }
}
