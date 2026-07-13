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

    // ---- Transaccionalidad (Formularios avanzados, ola F3; doc 01 D2/D3) ----

    /// <summary>Si es true, cada envio confirmado es un REGISTRO (hecho) con identidad, estado y fecha.</summary>
    public bool IsTransactional { get; set; }

    /// <summary>Como produce la identidad el registro (ninguna / clave natural / consecutivo).</summary>
    public FormIdentityMode IdentityMode { get; set; } = FormIdentityMode.None;

    /// <summary>NaturalKey: campo del formulario cuyo valor es el numero/clave del registro.</summary>
    public string? IdentitySourceFieldCode { get; set; }

    /// <summary>NaturalKey: codigos de campo que forman la clave unica por tenant (arreglo JSON).</summary>
    public string? UniqueKeyFieldsJson { get; set; }

    /// <summary>Sequence: referencia logica a la TenantSequence que se consume al confirmar (null si otro modo).</summary>
    public Guid? SequenceId { get; set; }

    // ---- Formulario como MODULO del sistema (Formularios avanzados, ola F4; doc 01 D1/D6) ----

    /// <summary>Si es true, el formulario es un modulo con nodo de menu propio y bandeja en /m/{code}.</summary>
    public bool IsModule { get; set; }

    /// <summary>Nodo de menu generado al promover a modulo (el usuario elige DONDE colgarlo). Null si no es modulo.</summary>
    public Guid? ModuleMenuNodeId { get; set; }

    /// <summary>Icono del modulo en el menu (clave de icono del prototipo).</summary>
    public string? ModuleIcon { get; set; }

    /// <summary>Columnas de la bandeja del modulo (arreglo JSON de field codes). Null = por defecto.</summary>
    public string? ListColumnsJson { get; set; }

    /// <summary>Campos de filtro de la bandeja (arreglo JSON de field codes).</summary>
    public string? FilterFieldsJson { get; set; }
}
