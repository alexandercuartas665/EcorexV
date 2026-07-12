namespace Ecorex.Domain.Enums;

/// <summary>
/// Ciclo de vida de un REGISTRO transaccional (Formularios avanzados, ola F3; doc 01 D2). Aplica
/// solo cuando la definicion es transaccional; es INDEPENDIENTE del <see cref="FormResponseStatus"/>
/// (Draft/Submitted, el ciclo de envio del borrador). Se persiste como string.
/// </summary>
public enum FormRecordStatus
{
    /// <summary>Borrador: aun no confirmado como hecho. Default.</summary>
    Draft = 0,

    /// <summary>Confirmado: consumio identidad (consecutivo o clave natural), es un hecho.</summary>
    Confirmed,

    /// <summary>Anulado: se conserva (no borra ni libera el consecutivo), con motivo y auditoria.</summary>
    Voided
}
