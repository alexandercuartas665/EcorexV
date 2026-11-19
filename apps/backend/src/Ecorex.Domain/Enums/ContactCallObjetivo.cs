namespace Ecorex.Domain.Enums;

/// <summary>
/// Objetivo de una llamada IA del paso "Llamada" del programador de acciones de Contactos (ADR-0056).
/// Es solo configuracion/referencia: el motor de voz IA es de una fase siguiente (aqui no se coloca
/// ninguna llamada ni se integra proveedor).
/// </summary>
public enum ContactCallObjetivo
{
    /// <summary>Ofrecer un producto/servicio al contacto.</summary>
    OfrecerProducto,

    /// <summary>Conseguir que el agente llene uno o varios formularios.</summary>
    LlenarFormulario,

    /// <summary>Objetivo libre descrito en la instruccion adicional al prompt.</summary>
    Personalizado,

    /// <summary>Ola 1 (ADR-0190): seguimiento/cierre con el cliente por voz. Es una MARCA, no una directiva
    /// pesada: el webhook NO vuelca a formulario y la conducta la lleva el prompt del agente de voz (SARA); el
    /// resultado (transcript) alimenta la RUTA de la decision, no un formulario. Usado por el nodo de
    /// seguimiento cuando NO tiene formulario asociado.</summary>
    SeguimientoCierre
}
