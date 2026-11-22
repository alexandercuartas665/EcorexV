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

    /// <summary>
    /// Quien abre el enlace puede ver los REGISTROS detras de un grafico ("Ver registros", ADR-0158 Ola 7c).
    /// Apagado por defecto: el enlace es anonimo y el detalle muestra filas individuales. Lo activa un Admin.
    /// </summary>
    public bool PermiteRegistros { get; set; }
}
