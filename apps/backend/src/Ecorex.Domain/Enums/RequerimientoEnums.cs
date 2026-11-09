namespace Ecorex.Domain.Enums;

/// <summary>De donde viene un origen de requerimientos (que alimenta las necesidades de compra).</summary>
public enum RequerimientoFuenteTipo
{
    /// <summary>Alta a mano dentro del modulo.</summary>
    Manual = 0,
    /// <summary>Un formulario del gestor de formularios entrega las necesidades.</summary>
    Formulario = 1,
    /// <summary>Un Contenedor de Datos alimenta las necesidades.</summary>
    Contenedor = 2
}

/// <summary>Dimension del item sobre la que una regla de la matriz asigna proveedor. El orden va de lo
/// MAS especifico (Referencia) a lo mas general (Categoria): gana la de menor valor cuando hay empate.</summary>
public enum RequerimientoDimension
{
    Referencia = 0,
    Marca = 1,
    Subgrupo = 2,
    Grupo = 3,
    Categoria = 4
}

/// <summary>Estado de una Solicitud de Cotizacion (RFQ) a lo largo de su ciclo: nace en Borrador, sale al
/// proveedor (Enviada), este contesta (Respondida), se lleva al comparador (Comparada) y, cuando el jefe de
/// compras arma la compra en planeacion, queda Adjudicada; Cerrada al terminar. Vencida/Cancelada son salidas.
/// El flujo de proceso solo mueve Borrador->Enviada->Respondida; comparar y adjudicar ocurren en planeacion.</summary>
public enum RfqEstado
{
    /// <summary>Creada; el formulario a enviar aun no sale al proveedor (Ola A crea la tarea portadora).</summary>
    Borrador = 0,
    /// <summary>El RFQ salio al proveedor (actividad/flujo en curso; en Ola B, el formulario /f enviado).</summary>
    Enviada = 1,
    /// <summary>El proveedor respondio (sus precios ya entraron al comparador).</summary>
    Respondida = 2,
    /// <summary>La oferta se esta comparando en planeacion.</summary>
    Comparada = 3,
    /// <summary>El jefe de compras adjudico (asigno la linea a este proveedor en planeacion).</summary>
    Adjudicada = 4,
    /// <summary>Cerrada.</summary>
    Cerrada = 5,
    /// <summary>La oferta vencio sin usarse.</summary>
    Vencida = 6,
    /// <summary>Cancelada.</summary>
    Cancelada = 7
}

/// <summary>Estado de un requerimiento (y de sus registros hijos) a lo largo del ciclo de compra.</summary>
public enum RequerimientoEstado
{
    /// <summary>Recien llegado de una fuente (o alta manual), sin procesar.</summary>
    Recibido = 0,
    /// <summary>Consolidado/agrupado con otros pedidos del mismo producto.</summary>
    Agrupado = 1,
    /// <summary>Dividido en registros hijos (granularidad) y programado en el tiempo.</summary>
    Planificado = 2,
    /// <summary>Con proveedor asignado, listo para ejecutar.</summary>
    ConProveedor = 3,
    /// <summary>Compra ejecutada: genero una actividad (tarea).</summary>
    Ejecutado = 4,
    /// <summary>Cerrado.</summary>
    Cerrado = 5,
    /// <summary>Enviado a la Orden de compra: la division queda BLOQUEADA (no editable) hasta que se gestione
    /// (emita) la compra desde la pestana Orden de compra. Se puede LIBERAR mientras no se haya emitido la tarea
    /// de gestion (una vez con actividad, pasa a Ejecutado y ya no se libera).</summary>
    EnOrdenCompra = 6
}
