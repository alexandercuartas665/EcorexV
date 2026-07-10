using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Cita / evento de la Agenda del Gestor de Clientes (000740). Se genera desde la gestion
/// "Contacto Cliente" del tercero (la accion "Proxima atencion") o directamente en la Agenda.
/// Aparece en el calendario de la pestana Agenda. TENANT-SCOPED.
/// </summary>
public class Cita : TenantEntity
{
    /// <summary>Cliente/contacto de la cita (opcional). Cascade: muere con el tercero.</summary>
    public Guid? TerceroId { get; set; }
    public Tercero? Tercero { get; set; }

    /// <summary>Oportunidad relacionada (opcional).</summary>
    public Guid? OportunidadId { get; set; }
    public Oportunidad? Oportunidad { get; set; }

    public string Titulo { get; set; } = null!;

    public CitaTipo Tipo { get; set; } = CitaTipo.Reunion;

    /// <summary>Inicio del evento (fecha + hora), en UTC; se muestra en la zona del tenant.</summary>
    public DateTimeOffset Inicio { get; set; }

    /// <summary>Duracion en minutos (0 = sin duracion definida).</summary>
    public int DuracionMinutos { get; set; }

    public string? Nota { get; set; }

    /// <summary>Marca la cita como atendida/hecha.</summary>
    public bool Completada { get; set; }
}
