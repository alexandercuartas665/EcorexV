using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Plantilla publicada en el MARKETPLACE (ADR-0097 Ola B2). Es de PLATAFORMA (BaseEntity, sin TenantId,
/// sin filtro global): el catalogo es el mismo para TODOS los tenants (lo LEEN todos), y solo el
/// PlatformAdmin lo ESCRIBE. Guarda un snapshot PORTABLE del flujo/formulario (FlowPackage o el export
/// del formulario) que un tenant "trae" para clonarlo en su propio tenant (Ola B3).
/// </summary>
public class MarketplaceItem : BaseEntity
{
    public MarketplaceItemKind Kind { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>Imagen del item: URL o data-URL (imagen chica embebida). Opcional.</summary>
    public string? ImageRef { get; set; }

    /// <summary>Etiqueta/categoria de PLATAFORMA para explorar el marketplace (independiente de las
    /// etiquetas por-tenant de la Ola A1).</summary>
    public string? Category { get; set; }

    /// <summary>Snapshot portable serializado (FlowPackage para Flow; export del formulario para Form).</summary>
    public string SnapshotJson { get; set; } = null!;

    public int SnapshotFormatVersion { get; set; } = 1;

    /// <summary>ProcessCode / FormCode de origen (informativo).</summary>
    public string? SourceCode { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Cuantas veces se ha "traido" (importado) a un tenant.</summary>
    public int ImportCount { get; set; }

    /// <summary>PlatformUser que publico (auditoria).</summary>
    public Guid? PublishedByPlatformUserId { get; set; }

    public DateTimeOffset PublishedAt { get; set; }
}
