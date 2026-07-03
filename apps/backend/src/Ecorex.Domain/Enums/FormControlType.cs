namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipos de control del constructor de formularios (port del catalogo EAV legacy, ADR-0015).
/// Tier 1 (con componente en DynamicFormRenderer): Text..Literal. Los restantes existen en el
/// enum para portar definiciones legacy sin perder el tipo, pero AUN no tienen componente:
/// el renderer los muestra como placeholder deshabilitado.
/// </summary>
public enum FormControlType
{
    // ---- Tier 1 (renderizables) ----
    /// <summary>Entrada de texto de una linea.</summary>
    Text = 0,
    /// <summary>Area de texto multi-linea.</summary>
    TextArea,
    /// <summary>Titulo/encabezado visual (no captura datos).</summary>
    Heading,
    /// <summary>Lista desplegable de opcion unica (OptionsJson).</summary>
    Select,
    /// <summary>Casillas de verificacion de opcion multiple (OptionsJson).</summary>
    MultiCheck,
    /// <summary>Botones de radio de opcion unica (OptionsJson).</summary>
    Radio,
    /// <summary>Interruptor booleano si/no.</summary>
    Toggle,
    /// <summary>Entrada numerica (rango via ValidationJson).</summary>
    Number,
    /// <summary>Selector de fecha.</summary>
    Date,
    /// <summary>Texto fijo informativo (no captura datos).</summary>
    Literal,

    // ---- Tiers posteriores (sin componente aun) ----
    Image,
    Photo,
    Audio,
    Signature,
    Gps,
    Button,
    Chart,
    GridDetail,
    Html
}
