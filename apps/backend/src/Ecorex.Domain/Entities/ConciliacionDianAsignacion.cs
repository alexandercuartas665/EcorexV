using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Evento del historial de asignaciones de un renglon de la Conciliacion DIAN de compras: registra cada vez
/// que el documento se direcciono a un usuario (<see cref="ConciliacionAsignacionAccion.Asignado"/>) o se
/// devolvio al conciliador (<see cref="ConciliacionAsignacionAccion.Rechazado"/>). Append-only: la grilla lo
/// usa para contar cuantas veces se direcciono y mostrar a quienes y cuando. Clave logica: (TenantId, RenglonId).
/// </summary>
public sealed class ConciliacionDianAsignacion : TenantEntity
{
    /// <summary>Renglon de conciliacion al que pertenece el evento.</summary>
    public Guid RenglonId { get; set; }
    public ConciliacionDianRenglon? Renglon { get; set; }

    /// <summary>Usuario (TenantUser) involucrado: el asignado (en Asignado) o el que tenia el documento al
    /// devolverlo (en Rechazado). null = sin usuario.</summary>
    public Guid? AssignedToTenantUserId { get; set; }

    /// <summary>Numero de la tarea/actividad relacionada, si la hubo (p. ej. la que se cancelo al devolver).</summary>
    public string? TaskNumber { get; set; }

    /// <summary>Tipo de evento.</summary>
    public ConciliacionAsignacionAccion Accion { get; set; }
}
