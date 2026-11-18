using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Proyecto de MAQUETA del tenant (ADR-0179): un sitio estatico (HTML/CSS/JS + anotador) que se sube por versiones
/// (ZIP) y se sirve bajo /maqueta/{Slug}/ para anotarlo; las anotaciones llegan como tarjetas (ADR-0176).
/// </summary>
public class MockupProject : TenantEntity
{
    /// <summary>Segmento de URL unico por tenant (minusculas, digitos y guion).</summary>
    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Version que se sirve en /maqueta/{Slug}/. Null hasta la primera subida.</summary>
    public Guid? CurrentVersionId { get; set; }

    /// <summary>Contador de versiones (la siguiente es LastVersionNumber + 1).</summary>
    public int LastVersionNumber { get; set; }

    /// <summary>Soft-delete (archivado): deja de listarse y de servirse; las versiones se conservan.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<MockupVersion> Versions { get; set; } = new List<MockupVersion>();
}
