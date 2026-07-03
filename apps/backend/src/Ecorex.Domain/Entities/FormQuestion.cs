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
}
