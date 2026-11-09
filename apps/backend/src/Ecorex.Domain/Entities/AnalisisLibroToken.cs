using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Enlace publico de solo lectura de un libro (/a/{token}, ADR-0158 D7, Ola 6). Solo se guarda el hash
/// SHA-256 del token (patron FormToken); rotable, revocable y con expiracion opcional.
/// </summary>
public sealed class AnalisisLibroToken : TenantEntity
{
    public Guid LibroId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
