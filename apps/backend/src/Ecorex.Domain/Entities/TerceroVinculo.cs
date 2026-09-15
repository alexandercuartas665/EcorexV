using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Vinculo Persona &lt;-&gt; Organizacion del Directorio Modular (Capa 8, regla 3.1). Relacion M:N con
/// CARGO POR VINCULO: una persona puede estar en varias organizaciones con cargos distintos, y una
/// organizacion tiene varias personas. Complementa el enlace primario legado <see cref="Tercero.EmpresaId"/>
/// (que se conserva para el motor Clasico y el contacto principal); el panel de relaciones muestra ambos.
/// Multi-tenant (filtro global por TenantId).
/// </summary>
public class TerceroVinculo : TenantEntity
{
    /// <summary>La persona (Tercero de tipo Persona) del vinculo.</summary>
    public Guid PersonaId { get; set; }
    public Tercero? Persona { get; set; }

    /// <summary>La organizacion (Tercero de tipo Empresa) del vinculo.</summary>
    public Guid OrganizacionId { get; set; }
    public Tercero? Organizacion { get; set; }

    /// <summary>Cargo de la persona EN ESTA organizacion (puede diferir de otros vinculos).</summary>
    public string? Cargo { get; set; }

    /// <summary>Marca el vinculo principal de la persona (el que se refleja en el enlace primario).</summary>
    public bool Principal { get; set; }
}
