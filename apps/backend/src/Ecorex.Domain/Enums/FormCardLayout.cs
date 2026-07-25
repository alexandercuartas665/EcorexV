namespace Ecorex.Domain.Enums;

/// <summary>
/// Ancho de la tarjeta del formulario al llenarlo (pagina publica /f, modulo /m y vista previa del
/// disenador). Es CONFIGURACION por formulario, no global: un cotizador con una tabla de 25 columnas
/// necesita mas ancho que un formulario de contacto. NO rota a apaisado; solo ensancha la tarjeta.
/// El default es <see cref="Normal"/> para no alterar los formularios existentes.
/// </summary>
public enum FormCardLayout
{
    /// <summary>Ancho actual (~720px), centrado. Lo que ya tenian todos los formularios.</summary>
    Normal = 0,
    /// <summary>Tarjeta ancha (~1160px) para formularios con tablas anchas.</summary>
    Ancho,
    /// <summary>Casi todo el ancho de la ventana (min(96vw, 1600px)).</summary>
    Completo
}
