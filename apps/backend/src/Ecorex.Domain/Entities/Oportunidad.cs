using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Oportunidad de negocio del Gestor de Clientes (000740). Nace DESDE un cliente/contacto
/// (<see cref="Tercero"/>): un tercero puede tener varias. Alimenta el kanban de la pestana
/// "Oportunidades" (por <see cref="Etapa"/>) y el panel de oportunidades abiertas de la ficha
/// del cliente. Cerrada = etapa Ganada o Perdida. TENANT-SCOPED.
/// </summary>
public class Oportunidad : TenantEntity
{
    /// <summary>Cliente/contacto del que sale la oportunidad. Cascade: muere con el tercero.</summary>
    public Guid TerceroId { get; set; }
    public Tercero? Tercero { get; set; }

    public string Nombre { get; set; } = null!;

    public OportunidadEtapa Etapa { get; set; } = OportunidadEtapa.Nueva;

    /// <summary>Valor estimado del negocio.</summary>
    public decimal Valor { get; set; }

    /// <summary>Responsable/KAM (texto libre; puede ser el vendedor asignado del tercero).</summary>
    public string? Responsable { get; set; }

    /// <summary>Probabilidad de cierre 0-100.</summary>
    public int Probabilidad { get; set; }

    public DateTimeOffset? FechaCierre { get; set; }

    /// <summary>Fuente/origen de la oportunidad (LinkedIn, Referido, Campana, etc.).</summary>
    public string? Fuente { get; set; }

    public string? Descripcion { get; set; }

    public int SortOrder { get; set; }
}
