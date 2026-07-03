using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Token opaco de publicacion de un formulario por URL (/f/{token}, ADR-0015). El token en
/// claro se muestra UNA sola vez al emitirlo; aqui solo se guarda su hash SHA-256 (hex, 64).
/// La resolucion del visor anonimo busca por hash con IgnoreQueryFilters (unico punto
/// cross-tenant permitido, acotado al hash exacto) y luego fija el tenant del token como
/// ambient para el resto del pipeline. TENANT-SCOPED.
/// </summary>
public class FormToken : TenantEntity
{
    /// <summary>SHA-256 (hex minusculas, 64 chars) del token opaco. Unico por tenant.</summary>
    public string TokenHash { get; set; } = null!;

    public Guid DefinitionId { get; set; }
    public FormDefinition? Definition { get; set; }

    /// <summary>Referencia externa que hereda la respuesta creada via este token.</summary>
    public string? Reference { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Si es true, el token queda inutilizable tras el primer envio (UsedAt).</summary>
    public bool SingleUse { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Permite responder sin sesion (visor publico /f/{token}).</summary>
    public bool AllowAnonymous { get; set; } = true;
}
