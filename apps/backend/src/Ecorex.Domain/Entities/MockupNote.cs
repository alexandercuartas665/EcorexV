using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Nota de un CEREBRO DIGITAL (ADR-0183; desde ADR-0200 cuelga de <see cref="Brain"/>, no de la maqueta): texto markdown al estilo Obsidian que se enlaza con
/// otras notas (<c>[[Titulo]]</c>), con pantallas de la maqueta (<c>[[pantalla.html]]</c>) y con solicitudes
/// (<c>[[T00046]]</c>). Los enlaces se derivan del cuerpo al guardar (<see cref="MockupNoteLink"/>).
/// Tenant-scoped + soft-delete (archivar) + concurrencia optimista.
/// </summary>
public sealed class MockupNote : TenantEntity, ISoftDeletable, IVersioned
{
    public Guid BrainId { get; set; }

    public Brain? Brain { get; set; }

    /// <summary>Titulo visible (max 200). Unico por cerebro entre las notas activas (sin distinguir mayusculas).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Titulo normalizado (minusculas, espacios colapsados): la llave con la que se resuelven los enlaces.</summary>
    public string TitleKey { get; set; } = string.Empty;

    /// <summary>Cuerpo en markdown (texto largo).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Usuario del tenant que creo la nota (puede archivarla ademas de Owner/Admin).</summary>
    public Guid? AuthorTenantUserId { get; set; }

    public string? AuthorName { get; set; }

    public string? UpdatedByName { get; set; }

    /// <summary>Ruta del .md de origen cuando la nota llego importando un vault de Obsidian.</summary>
    public string? ImportedFrom { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedByTenantUserId { get; set; }

    public long Version { get; set; }
}

/// <summary>
/// Enlace DERIVADO de una nota (ADR-0183): se reescribe en la misma transaccion en que se guarda la nota. Permite los
/// backlinks ("enlazan aqui") y el grafo con una sola consulta. Los enlaces a notas se resuelven por
/// <see cref="TargetKey"/> = <see cref="MockupNote.TitleKey"/>, asi un enlace a una nota que aun no existe ("fantasma")
/// queda resuelto solo cuando se crea.
/// </summary>
public sealed class MockupNoteLink : TenantEntity
{
    public Guid BrainId { get; set; }

    public Guid FromNoteId { get; set; }

    public MockupNote? FromNote { get; set; }

    /// <summary>note | page | task | tag (ver MockupBrainLinkKinds).</summary>
    public string TargetKind { get; set; } = string.Empty;

    /// <summary>Titulo normalizado, archivo .html, numero de tarjeta (T00046) o etiqueta, segun el tipo.</summary>
    public string TargetKey { get; set; } = string.Empty;

    /// <summary>Texto como se escribio (para mostrar el nodo fantasma con su titulo original).</summary>
    public string? Label { get; set; }
}
