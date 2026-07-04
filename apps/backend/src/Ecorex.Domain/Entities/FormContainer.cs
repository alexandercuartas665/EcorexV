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

    // ---- Constructor del prototipo (ADR-0021) ----

    /// <summary>Nombres de las pestanas cuando ContainerType es Tabs (arreglo JSON de strings).</summary>
    public string? TabsJson { get; set; }

    /// <summary>Ancho en columnas de la grilla de 12 del constructor (1..12).</summary>
    public int Width { get; set; } = 12;

    /// <summary>Fijo en el layout: el constructor no permite reordenarlo (prototipo lock).</summary>
    public bool IsLocked { get; set; }

    /// <summary>Oculto: ni el contenedor ni su subarbol se pintan en el renderer (prototipo eye).</summary>
    public bool IsHidden { get; set; }
}
