using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// GRUPO (categoria) de plantillas de documento. Agrupa <see cref="DocumentTemplate"/> para poder
/// asignar a un concepto/actividad (000270) uno o varios grupos que la tarea podra usar al redactar un
/// documento. Entidad TENANT-SCOPED. Ej. "Cartas", "Cotizaciones", "Actas".
/// </summary>
public class DocumentTemplateGroup : TenantEntity
{
    /// <summary>Nombre visible del grupo (unico por tenant). Ej. "Cartas de cobro".</summary>
    public string Name { get; set; } = null!;

    /// <summary>Descripcion opcional del grupo.</summary>
    public string? Description { get; set; }

    /// <summary>Grupo activo (los inactivos no se ofrecen al crear documentos, pero se conservan).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Orden de aparicion.</summary>
    public int SortOrder { get; set; }

    /// <summary>Plantillas de este grupo.</summary>
    public ICollection<DocumentTemplate> Templates { get; set; } = new List<DocumentTemplate>();
}
