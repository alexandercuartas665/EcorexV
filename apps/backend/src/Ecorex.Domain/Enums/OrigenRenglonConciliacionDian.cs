namespace Ecorex.Domain.Enums;

/// <summary>
/// Origen de un renglon de la Conciliacion DIAN: de donde salio la fila de la parrilla.
/// <see cref="Dian"/> es el caso normal (factura descargada de la DIAN / bot). <see cref="Newton"/>
/// marca un renglon "fantasma": un documento que existe en el proveedor tecnologico NEWTON pero que NO
/// se descargo de la DIAN en el periodo. Permite compararlos en la grilla y filtrarlos sin ensuciar el
/// conteo real: un fantasma se PROMUEVE a <see cref="Dian"/> si mas tarde la DIAN trae ese mismo CUFE.
/// </summary>
public enum OrigenRenglonConciliacionDian
{
    /// <summary>Factura descargada de la DIAN (bot). Caso por defecto.</summary>
    Dian = 0,

    /// <summary>Solo esta en NEWTON (no llego de la DIAN en el periodo): renglon fantasma.</summary>
    Newton = 1
}
