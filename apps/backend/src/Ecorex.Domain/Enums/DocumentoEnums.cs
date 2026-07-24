namespace Ecorex.Domain.Enums;

/// <summary>
/// De donde salio el documento. Portado del hermano PROPIA (modulo 2.15) y ADAPTADO al dominio de
/// ECOREX: alli los origenes eran los modulos de una copropiedad (asamblea, pqrsd, porteria,
/// mantenimiento); aqui son los modulos que de verdad producen archivos en este producto.
/// <see cref="OrigenEntidadId"/> del documento guarda el id del registro que lo genero.
/// </summary>
public enum OrigenDocumento
{
    /// <summary>Lo subio una persona desde el gestor documental.</summary>
    Manual,
    /// <summary>Adjunto nacido en una tarea (TaskItem).</summary>
    Tarea,
    /// <summary>Adjunto producido al ejecutar un paso de un flujo.</summary>
    Flujo,
    /// <summary>Archivo cargado al responder un formulario dinamico.</summary>
    Formulario,
    /// <summary>Documento asociado a un proyecto.</summary>
    Proyecto,
    /// <summary>Llego por un canal de conversacion (WhatsApp, chat).</summary>
    Comunicacion,
    /// <summary>Lo genero el motor (plantilla, reporte, exportacion).</summary>
    Sistema
}

/// <summary>
/// Ciclo de vida del documento. Borrador -> Vigente -> Archivado es el recorrido soportado.
/// EnFirma y Firmado quedan RESERVADOS para cuando exista firma electronica: se declaran para no
/// tener que migrar la columna despues, pero ninguna transicion los produce todavia.
/// </summary>
public enum EstadoDocumento
{
    Borrador,
    Vigente,
    /// <summary>Reservado: firma electronica no implementada.</summary>
    EnFirma,
    /// <summary>Reservado: firma electronica no implementada.</summary>
    Firmado,
    Archivado
}

/// <summary>
/// Evento de la bitacora del documento (tabla append-only, ver reglas 5 y 8 de CLAUDE.md).
/// Nunca se actualiza ni se borra una fila: cada cambio agrega una.
/// </summary>
public enum TipoEventoDocumento
{
    Subida,
    NuevaVersion,
    Vista,
    Descarga,
    CambioMetadatos,
    CambioCategoria,
    CambioCarpeta,
    CambioEtiquetas,
    CambioVisibilidad,
    CambioEstado,
    Archivado,
    Reactivado,
    /// <summary>Soft-delete: el documento queda inactivo, el archivo NO se borra.</summary>
    EliminacionLogica
}

/// <summary>Como se consumio el documento. Alimenta las estadisticas de uso.</summary>
public enum TipoEventoConsumo
{
    Vista,
    Descarga
}

/// <summary>Desde que clase de dispositivo se consumio. Unknown cuando no se puede determinar.</summary>
public enum DispositivoConsumo
{
    Mobile,
    Desktop,
    Unknown
}

/// <summary>
/// Quien puede ver el documento. En PROPIA era texto libre ("PRIVADO"/"EQUIPO"/"PUBLICO"), lo que
/// permite guardar cualquier cosa; aqui es un enum para que el valor sea verificable.
/// </summary>
public enum VisibilidadDocumento
{
    /// <summary>Solo quien lo subio y los Owner/Admin del tenant.</summary>
    Privado,
    /// <summary>Todo el equipo del tenant (por defecto).</summary>
    Equipo,
    /// <summary>Cualquiera con acceso al modulo, incluidos perfiles limitados.</summary>
    Publico
}
