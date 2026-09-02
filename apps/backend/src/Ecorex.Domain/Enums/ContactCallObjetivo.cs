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
    Personalizado
}
