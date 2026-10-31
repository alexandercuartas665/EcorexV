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
    Subform,

    /// <summary>
    /// Configurador en cascada + tablas dinamicas (motor generico, config-driven): N niveles
    /// encadenados (single/multi, con dependencia padre-hijo), juegos de columnas y mapeo rama->tabla,
    /// TODO leido desde <see cref="Ecorex.Domain.Entities.FormQuestion.CascadeConfigJson"/> (nada
    /// hardcodeado; el mismo motor sirve cualquier taxonomia de cualquier tenant). Las tablas reusan
    /// el pipeline de calculo de GridDetail (FormExpressionEvaluator / FormGridCalculator). El registro
    /// capturado se persiste en FormResponse.Data como cualquier otro formulario.
    /// </summary>
    CascadeConfigurator,

    /// <summary>
    /// Campo geografico Colombia (Pais/Departamento/Ciudad) auto-encadenado: al ponerlo en el
    /// formulario pinta 3 selects en cascada (Pais fijo Colombia -> Departamento -> Ciudad) leidos
    /// del catalogo global DANE (<see cref="Ecorex.Domain.Entities.Ciudad"/>), sin configuracion.
    /// El valor se persiste en FormResponse.Data como JSON {pais,departamento,ciudad}.
    /// </summary>
    Geografia,

    /// <summary>
    /// Lienzo de CROQUIS/DIBUJO (ADR Canvas): reemplaza la parrilla milimetrada del formato en papel por un
    /// editor donde se dibujan rectangulos, elipses, texto, imagenes y trazo libre sobre una cuadricula clara.
    /// El dibujo se guarda en FormResponse.Data como un SVG autocontenido (imagenes embebidas como data-URL);
    /// FormFieldValue.Type = "Canvas". Motor JS propio (form-canvas.js). Se agrego AL FINAL del enum para
    /// preservar los ordinales de los valores anteriores.
    /// </summary>
    Canvas,

    /// <summary>
    /// MATRIZ FIJA (formatos tipo DIAN 350): filas (conceptos) y columnas (agrupables) PREDEFINIDAS en OptionsJson
    /// como OBJETO {rows, cols, captions, disabled} (<c>FixedMatrixSpec</c>); una casilla por celda con su numero.
    /// El valor se guarda en FormResponse.Data como objeto JSON plano {"fila.col": "valor"}. Reemplaza la
    /// simulacion fragil con decenas de Rows + campos sueltos alineados por width. Al FINAL del enum para preservar
    /// los ordinales.
    /// </summary>
    FixedMatrix,

    /// <summary>
    /// DOCUMENTOS DE LA TAREA (solo lectura): muestra, dentro del formulario, los archivos adjuntos de la TAREA a la
    /// que pertenece el registro (se resuelve por FormResponse.Reference = numero de tarea). Es el "visor de
    /// documentos" de la tarea, embebido en el formulario, para gestionar desde aqui. No captura valor. Al FINAL del
    /// enum para preservar los ordinales.
    /// </summary>
    TaskDocuments,

    /// <summary>
    /// ASISTENTE DE REQUERIMIENTO (IA, imagen/PDF -> tabla): control drag-and-drop que es un AGENTE IA con una
    /// herramienta ACOTADA AL MISMO formulario. Lee el requerimiento del cliente (foto/PDF) y PROPONE filas para
    /// una grilla de ESTE formulario (el humano confirma antes de escribir). Config en OptionsJson
    /// (<c>AiIntakeConfig</c>): agente, prompt, grilla destino, accept, max, modo. No captura valor por si mismo
    /// (escribe en la grilla destino). Al FINAL del enum para preservar los ordinales.
    /// </summary>
    AiIntake,

    /// <summary>
    /// LIENZO DE BLOQUES (preset Business Model Canvas): grilla de bloques con etiqueta + texto editable. El autor
    /// define los bloques (id/label/posicion/span/acento + source opcional); BMC es un preset de 9 bloques. Captura
    /// valor: un objeto JSON { blockId: texto } en FormResponse.Data (reportable). Con 'source' (field codes del
    /// mismo formulario) el bloque se pre-llena concatenando esas respuestas y queda editable. NO es el Canvas
    /// (croquis/dibujo) de la OT. Al FINAL del enum para preservar los ordinales.
    /// </summary>
    LienzoBloques,

    /// <summary>
    /// ASISTENTE IA (llenar campos): primo de AiIntake, pero NO sube imagen/PDF: lee las respuestas ya capturadas
    /// del propio formulario (FormResponse.Data) y PROPONE valores para CAMPOS y/o BLOQUES de un LienzoBloques
    /// (targets). Reusa set_campo y la nueva set_bloque del FormScopedToolset (ACOTADO a la definicion), el agente
    /// del tenant, cupo y AiUsageLog. Config en OptionsJson (<c>AiComposeConfig</c>): agente, prompt, sources,
    /// targets, buttonLabel, confirm, model. No captura valor por si mismo (escribe en los targets). Al FINAL del
    /// enum para preservar los ordinales.
    /// </summary>
    AiCompose,

    /// <summary>
    /// BOTON DE ACCION (self-serve): boton colocable en cualquier parte del formulario cuya accion se configura
    /// 100% desde el disenador (sin regla de catalogo, a diferencia de <see cref="Button"/>). Config en OptionsJson
    /// (<c>ActionButtonConfig</c>): action (p.ej. "print-lienzo"), targetField (field_code del LienzoBloques a
    /// imprimir, opcional -> el primero del formulario) y label. Al hacer clic abre la vista FormPrintLienzo del
    /// lienzo elegido en una pestana nueva (guarda el borrador antes). No captura valor. Al FINAL del enum para
    /// preservar los ordinales.
    /// </summary>
    ActionButton
}
