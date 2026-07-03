using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion de un formulario dinamico (port del constructor EAV legacy, ADR-0015).
/// El arbol contenedores -> preguntas cuelga por DefinitionId; las respuestas se guardan
/// como documento JSON por respuesta (FormResponse), no como filas EAV. TENANT-SCOPED,
/// con concurrencia optimista portable (Version, ADR-0013).
/// IMPORTANTE: la version DE NEGOCIO del formulario es <see cref="Revision"/>; la columna
/// Version (long) es el token de concurrencia de IVersioned y la incrementa el interceptor.
/// No comparten nombre a proposito para no chocar en el mapeo.
/// </summary>
public class FormDefinition : TenantEntity, IVersioned
{
    /// <summary>Codigo legible unico por tenant (ej. "FRM-001").</summary>
    public string Code { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>
    /// Version de negocio del formulario: arranca en 1 y se incrementa al guardar cambios
    /// estructurales (contenedores/preguntas) sobre una definicion Active (snapshot logico).
    /// </summary>
    public int Revision { get; set; } = 1;

    public FormStatus Status { get; set; } = FormStatus.Draft;

    /// <summary>Soft-archive: fuera de las listas por defecto, conserva historia.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Token de concurrencia optimista portable (lo incrementa el interceptor).</summary>
    public long Version { get; set; }
}
