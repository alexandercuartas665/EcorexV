using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Pregunta (campo) de un formulario dinamico (ADR-0015). FieldCode es la clave del campo
/// dentro del documento JSON de respuestas ({ fieldCode: { value, type } }) y es unico por
/// definicion. FK a la definicion en cascada; al contenedor NO ACTION (borrar un contenedor
/// exige decidir que pasa con sus preguntas en el servicio). TENANT-SCOPED.
/// </summary>
public class FormQuestion : TenantEntity
{
    public Guid DefinitionId { get; set; }
    public FormDefinition? Definition { get; set; }

    /// <summary>Contenedor al que pertenece (null = raiz del formulario).</summary>
    public Guid? ContainerId { get; set; }
    public FormContainer? Container { get; set; }

    /// <summary>Clave del campo en el JSON de respuestas. Unica por definicion.</summary>
    public string FieldCode { get; set; } = null!;

    public string Label { get; set; } = null!;

    /// <summary>Subtitulo corto bajo la etiqueta.</summary>
    public string? Caption { get; set; }

    /// <summary>Texto de ayuda (tooltip / hint).</summary>
    public string? HelpText { get; set; }

    public FormControlType ControlType { get; set; } = FormControlType.Text;

    /// <summary>Opciones para Select/MultiCheck/Radio: [{"id","label","value"}] (jsonb / nvarchar segun motor).</summary>
    public string? OptionsJson { get; set; }

    public bool Required { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Columna del grid bootstrap del renderer (ej. "col-md-6").</summary>
    public string GridCol { get; set; } = "col-12";

    /// <summary>Numeral impreso junto a la etiqueta (ej. "2.1", port del legacy).</summary>
    public string? Numeral { get; set; }

    /// <summary>Reglas de validacion: {"minLength","maxLength","pattern","minValue","maxValue"}.</summary>
    public string? ValidationJson { get; set; }

    // ---- Constructor del prototipo (ADR-0021) ----

    /// <summary>
    /// Ancho en columnas de la grilla de 12 del constructor (1..12). Fuente de verdad del
    /// layout; <see cref="GridCol"/> se mantiene SINCRONIZADO (col-12 / col-md-N) para no
    /// romper el renderer bootstrap ni los selectores E2E existentes.
    /// </summary>
    public int Width { get; set; } = 12;

    /// <summary>Texto de ayuda dentro del control (placeholder del input, prototipo 'ph').</summary>
    public string? PlaceholderText { get; set; }

    /// <summary>
    /// Valor por defecto del campo. DOBLE USO documentado (ADR-0021): en Paragraph es el
    /// texto del parrafo y en Spacer es el alto en px; en controles de captura es el valor
    /// inicial del borrador.
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>Fijo en el layout: el constructor no permite reordenarlo (prototipo lock).</summary>
    public bool IsLocked { get; set; }

    /// <summary>Oculto: no se pinta en el renderer y no valida requerido (prototipo eye).</summary>
    public bool IsHidden { get; set; }

    // ---- Origen de datos / lookup (Formularios avanzados, ola F1; doc 01 seccion D4) ----

    /// <summary>
    /// De donde salen las opciones/valores del campo: fijas (OptionsJson, default) o desde una
    /// tabla de datos con dominio del tenant (contenedor, terceros, items). Persistido como string.
    /// </summary>
    public FormSourceKind SourceKind { get; set; } = FormSourceKind.Options;

    /// <summary>
    /// Referencia de la fuente cuando SourceKind != Options: id del contenedor de datos (Guid) o
    /// clave de la entidad objetivo. String para admitir ambos sin romper el DAL dual.
    /// </summary>
    public string? SourceRef { get; set; }

    /// <summary>Campo de la fuente que se MUESTRA al usuario (ej. "Nombre", "Descripcion").</summary>
    public string? DisplayField { get; set; }

    /// <summary>Campo de la fuente que se GUARDA como valor del campo (el id). Es la dimension del hecho.</summary>
    public string? ValueField { get; set; }

    /// <summary>
    /// Scope del catalogo como JSON (ej. {"EsCliente": true}). Siempre acotado al tenant por el
    /// filtro global; se traduce a consulta parametrizada (nunca SQL concatenado). jsonb / nvarchar(max).
    /// </summary>
    public string? FilterJson { get; set; }

    /// <summary>
    /// Mapa de autollenado como JSON: campo de la fuente -> campo del formulario
    /// (ej. {"NIT":"nit","Ciudad":"ciudad"}). Al elegir una opcion se COPIAN (snapshot) esos
    /// valores a los campos destino. Incluye campos dinamicos de Tercero/Item. jsonb / nvarchar(max).
    /// </summary>
    public string? AutofillMapJson { get; set; }

    /// <summary>Como se ofrece el catalogo al llenar: autocompletar (default), lista o buscador modal.</summary>
    public FormFieldPresentation Presentation { get; set; } = FormFieldPresentation.Autocomplete;

    /// <summary>
    /// Maestro-detalle (ola F5, doc 01 D7): definicion HIJA que este campo Subform embebe. Los
    /// registros hijos se enlazan al padre por FormRecordLink. Null salvo en campos Subform.
    /// </summary>
    public Guid? SubformDefinitionId { get; set; }

    // ---- Transversales (Formularios avanzados, ola F6; doc 01 D8) ----

    /// <summary>Valor por defecto DINAMICO (Hoy / Usuario actual / ...), resuelto al abrir a llenar.</summary>
    public FormDefaultDynamic DefaultDynamic { get; set; } = FormDefaultDynamic.None;

    /// <summary>Formato/mascara de presentacion del valor: currency | percent | integer | phone | ... (null = sin formato).</summary>
    public string? Format { get; set; }

    /// <summary>
    /// Permisos por campo (ola F6, doc 01 D8) como JSON: { "hide": ["Advisor"], "readonly": ["Supervisor"] }
    /// (nombres de TenantRole). Un rol en hide no ve el campo; en readonly lo ve pero no lo edita. Null = sin
    /// restriccion. En el visor publico (sin rol) no aplica.
    /// </summary>
    public string? FieldVisibilityJson { get; set; }

    // ---- Calculo y agregacion (Formularios avanzados, ola F2; doc 01 seccion D5) ----

    /// <summary>
    /// Expresion de campo calculado sobre otros campos (aritmetica/condicional), ej.
    /// "{cantidad} * {precio} * (1 - {descuento})". Evaluada en un sandbox tipado (allow-list,
    /// sin codigo arbitrario); en el render el campo es de solo lectura. Null = campo normal.
    /// </summary>
    public string? CalcExpression { get; set; }

    /// <summary>
    /// Agregado de la columna cuando la pregunta es una columna de un GridDetail (doc 01 D5):
    /// pinta la fila de totales y alimenta el roll-up al encabezado. None = sin total.
    /// </summary>
    public FormAggregate Aggregate { get; set; } = FormAggregate.None;
}
