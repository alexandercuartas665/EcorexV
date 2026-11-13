using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Tipo de recurso agendable del tenant (ADR-0165): Persona, Grupo, Espacio, Equipo, Vehiculo, Punto de
/// atencion... Lo crea cada tenant; el sistema solo sugiere unos por defecto. Define el icono/color con que se
/// pinta y a que se vincula el recurso (<see cref="Vinculo"/>). Tenant-scoped + soft-delete.
/// </summary>
public sealed class AgendaTipoRecurso : TenantEntity, ISoftDeletable
{
    /// <summary>Nombre visible. Unico por tenant entre los no borrados. varchar(80).</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Clave de icono (persona, grupo, espacio, equipo, vehiculo, punto). varchar(30).</summary>
    public string Icono { get; set; } = "persona";

    /// <summary>Clave de color de etiqueta del prototipo (blue, green, amber, rose, violet, slate). varchar(20).</summary>
    public string Color { get; set; } = "blue";

    /// <summary>A que se vincula un recurso de este tipo.</summary>
    public AgendaVinculo Vinculo { get; set; } = AgendaVinculo.Libre;

    public int Orden { get; set; }

    public bool Activo { get; set; } = true;

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }
}
