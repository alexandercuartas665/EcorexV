using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Constancia de aceptacion de una politica de confidencialidad (ADR-0160). Registro INMUTABLE
/// (append-only, sin soft-delete): quien abre el link publico /pc/{token} declara su nombre e
/// identificacion y acepta. Se guarda un snapshot de la version y del hash del contenido aceptado,
/// de modo que un edit posterior de la politica no tergiverse lo que la persona realmente acepto.
/// TENANT-SCOPED (el tenant se fija por el token antes de insertar).
/// </summary>
public class ConfidentialityPolicyAcceptance : TenantEntity
{
    public Guid PolicyId { get; set; }
    public ConfidentialityPolicy? Policy { get; set; }

    public string Nombre { get; set; } = null!;
    public string Identificacion { get; set; } = null!;

    /// <summary>Correo opcional del aceptante (no obligatorio en el formulario publico).</summary>
    public string? Correo { get; set; }

    public DateTimeOffset AceptadoAt { get; set; }

    /// <summary>IP de origen (si el proxy la expone); referencia, no identificacion fuerte.</summary>
    public string? Ip { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>Version de la politica vigente al momento de aceptar.</summary>
    public long PolicyVersionSnapshot { get; set; }

    /// <summary>SHA-256 (hex, 64) del contenido exacto que se acepto.</summary>
    public string ContenidoHashSnapshot { get; set; } = null!;
}
