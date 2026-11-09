using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Libro de indicadores (ADR-0158 D6): agrupa hojas con widgets. Tenant-scoped por el filtro global,
/// borrado logico y concurrencia optimista portable (Version, ADR-0013).
/// </summary>
public sealed class AnalisisLibro : TenantEntity, ISoftDeletable, IVersioned
{
    public string Nombre { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    /// <summary>Usuario de plataforma que creo el libro (dueno). Solo el dueno o un Admin editan.</summary>
    public Guid? OwnerUserId { get; set; }

    public AnalisisVisibilidad Visibilidad { get; set; } = AnalisisVisibilidad.Privado;

    /// <summary>Lista opcional de usuarios (PlatformUserId) con acceso de lectura, JSON array (patron ADR-0117).</summary>
    public string? AllowedUserIdsJson { get; set; }

    public long Version { get; set; }

    public List<AnalisisHoja> Hojas { get; set; } = new();

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }
}
