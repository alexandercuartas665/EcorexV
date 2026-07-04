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
}
