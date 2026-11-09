using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Politica de confidencialidad del tenant, publicada por un link PERMANENTE (/pc/{token}, ADR-0160).
/// Hay UNA politica activa por tenant. El token es un recurso PUBLICO (no secreto), por lo que se
/// guarda EN CLARO -misma convencion que Tenant.PublicBookingToken-; es un valor aleatorio de 256
/// bits (inenumerable) que el admin copia cuantas veces quiera. La resolucion del visor anonimo
/// busca por PublicToken exacto con IgnoreQueryFilters (unico punto cross-tenant permitido) y luego
/// fija el tenant como ambient para registrar la aceptacion. El link NO vence (no hay ExpiresAt).
/// TENANT-SCOPED, soft-delete y concurrencia optimista (ADR-0013).
/// </summary>
public class ConfidentialityPolicy : TenantEntity, IVersioned, ISoftDeletable
{
    /// <summary>Titulo mostrado en la pagina publica.</summary>
    public string Titulo { get; set; } = "Politica de confidencialidad";

    /// <summary>Cuerpo de la politica (HTML saneado).</summary>
    public string Contenido { get; set; } = string.Empty;

    /// <summary>Token opaco EN CLARO del link permanente (base64url, 256 bits). Recurso publico.</summary>
    public string PublicToken { get; set; } = null!;

    /// <summary>Si es false, el link publico responde con el mensaje neutro (deshabilitado).</summary>
    public bool Activa { get; set; } = true;

    /// <summary>
    /// Si es true, el visor publico pide nombre e identificacion ademas del checkbox de aceptacion.
    /// Si es false (por defecto), la aceptacion es SOLO el checkbox (el aprobado del cliente), sin
    /// capturar datos personales. En ambos casos queda constancia (fecha, version y hash del contenido).
    /// </summary>
    public bool CapturaDatos { get; set; }

    // IVersioned
    public long Version { get; set; }

    // ISoftDeletable
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }

    public ICollection<ConfidentialityPolicyAcceptance> Acceptances { get; set; } = new List<ConfidentialityPolicyAcceptance>();
}
