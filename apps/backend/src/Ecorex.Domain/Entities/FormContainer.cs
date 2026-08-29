using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Contenedor del arbol de un formulario dinamico (segmento o tabla, ADR-0015). Arbol por
/// ParentId (self-FK NO ACTION: el servicio borra el subarbol explicitamente); vive y muere
/// con su definicion (FK cascade). TENANT-SCOPED.
/// </summary>
public class FormContainer : TenantEntity
{
    public Guid DefinitionId { get; set; }
    public FormDefinition? Definition { get; set; }

    public string Name { get; set; } = null!;

    public FormContainerType ContainerType { get; set; } = FormContainerType.Segment;

    /// <summary>Contenedor padre (null = raiz). Self-FK NO ACTION, nunca cascada.</summary>
    public Guid? ParentId { get; set; }
    public FormContainer? Parent { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Estilo visual opcional (clases/inline segun el renderer).</summary>
    public string? Style { get; set; }

    /// <summary>Etiquetas en linea: el label se pinta al frente del valor (misma linea, label a la
    /// izquierda con ancho fijo y control llenando el resto), en vez de arriba. Config-driven por
    /// contenedor (Row/Col). Default false = comportamiento actual (label arriba).</summary>
    public bool InlineLabels { get; set; }

    // ---- Constructor del prototipo (ADR-0021) ----

    /// <summary>Nombres de las pestanas cuando ContainerType es Tabs (arreglo JSON de strings).</summary>
    public string? TabsJson { get; set; }

    /// <summary>Ancho en columnas de la grilla de 12 del constructor (1..12).</summary>
    public int Width { get; set; } = 12;

    /// <summary>Fijo en el layout: el constructor no permite reordenarlo (prototipo lock).</summary>
    public bool IsLocked { get; set; }

    /// <summary>Oculto: ni el contenedor ni su subarbol se pintan en el renderer (prototipo eye).</summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Acceso por CARGO (ADR-0082): arreglo JSON de Guids de OrgUnit con Classifier==Cargo autorizados a
    /// OPERAR esta seccion. Null/vacio = sin restriccion (todos la operan, comportamiento actual). Un usuario
    /// cuyo cargo NO este en la lista VE la seccion en SOLO-LECTURA. Owner/Admin del tenant la operan siempre.
    /// </summary>
    public string? AllowedCargosJson { get; set; }

    /// <summary>
    /// Visibilidad CONDICIONAL de la SECCION por el VALOR de una pregunta (mismo esquema que
    /// <see cref="FormQuestion.VisibleWhenJson"/>): JSON { "field","op","value" }. Si no se cumple, ni la
    /// seccion ni su subarbol se pintan ni se exigen. Null = siempre visible. Se evalua en vivo en el renderer.
    /// </summary>
    public string? VisibleWhenJson { get; set; }
}
