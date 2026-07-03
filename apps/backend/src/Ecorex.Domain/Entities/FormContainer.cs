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
}
