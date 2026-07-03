using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>Agencia turistica cliente del SaaS. Entidad global administrada por el Super Admin.</summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = null!;
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? Country { get; set; }
    public string? Currency { get; set; }

    /// <summary>Ruta del logo de la agencia (subido por el cliente), p.ej. /uploads/tenant-{id}.png.</summary>
    public string? LogoUrl { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Trial;
    public TenantKind Kind { get; set; } = TenantKind.Standard;

    /// <summary>Reservas online por link publico habilitadas (columna legado del backbone; sin uso en ECOREX Tareas).</summary>
    public bool OnlineBookingEnabled { get; set; }

    /// <summary>Token opaco del link publico de reserva (/r/{token}). Se genera al habilitar. Unico.</summary>
    public string? PublicBookingToken { get; set; }

    /// <summary>Base publica del link (ej. https://beauty.ecorex.com.co), capturada al habilitar desde la
    /// consola. La usa el agente (sin request HTTP) para armar el link completo.</summary>
    public string? PublicBookingBaseUrl { get; set; }
}
