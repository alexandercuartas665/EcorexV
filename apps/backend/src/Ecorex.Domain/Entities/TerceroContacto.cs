using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Contacto embebido de una Empresa del Directorio General (000232): persona de
/// contacto "nativa" de la empresa (no es un Tercero independiente). Distinto de una
/// Persona natural reasignada a la empresa (esa es un <see cref="Tercero"/> con
/// EmpresaId). Multi-tenant.
/// </summary>
public class TerceroContacto : TenantEntity
{
    /// <summary>La empresa (Tercero de tipo Empresa) a la que pertenece este contacto.</summary>
    public Guid TerceroId { get; set; }
    public Tercero? Tercero { get; set; }

    public string Nombre { get; set; } = null!;
    public string? Cargo { get; set; }
    public string? Email { get; set; }
    public string? Telefono { get; set; }
}
