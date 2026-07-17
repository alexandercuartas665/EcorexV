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
    /// <summary>Selector de hora (input type=time). El valor se guarda como texto "HH:mm".</summary>
    Time,
    /// <summary>Selector de fecha y hora (input type=datetime-local).</summary>
    DateTime,
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
    /// <summary>Tabla de captura de filas dinamicas (prototipo 'tabla'). FUNCIONAL desde
    /// ADR-0021: columnas en OptionsJson ([{id,label}]) y el valor del campo es un arreglo
    /// JSON de filas [{colId: "valor"}].</summary>
    GridDetail,
    Html,

    // ---- Constructor del prototipo (ADR-0021; se persisten como string) ----
    /// <summary>Archivo adjunto (prototipo 'archivo'). Placeholder visual sin captura aun.</summary>
    File,
    /// <summary>Codigo de barras (prototipo 'barras'). Placeholder visual sin captura aun.</summary>
    Barcode,
    /// <summary>Parrafo de documento (prototipo 'parrafo'): texto estatico en DefaultValue;
    /// no captura datos.</summary>
    Paragraph,
    /// <summary>Linea divisoria horizontal (prototipo 'divisor'); no captura datos.</summary>
    Divider,
    /// <summary>Espaciado vertical (prototipo 'espacio'): alto en px en DefaultValue;
    /// no captura datos.</summary>
    Spacer,

    /// <summary>
    /// Maestro-detalle (Formularios avanzados, ola F5, doc 01 D7): el campo embebe registros HIJOS
    /// de OTRA definicion (FormQuestion.SubformDefinitionId), enlazados al registro padre por
    /// FormRecordLink. A diferencia de GridDetail (filas en el jsonb del padre), cada hijo es un
    /// FormResponse propio, reportable aparte.
    /// </summary>
    Subform
}
