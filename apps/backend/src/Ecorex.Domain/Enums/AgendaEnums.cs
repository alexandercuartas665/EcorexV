namespace Ecorex.Domain.Enums;

/// <summary>
/// A que se vincula un recurso agendable de un tipo dado (ADR-0165). El tipo lo decide; el recurso guarda
/// la referencia concreta. Se persiste como texto.
/// </summary>
public enum AgendaVinculo
{
    /// <summary>Recurso libre: no apunta a nada del sistema (una sala, una maquina, una linea).</summary>
    Libre = 0,

    /// <summary>Apunta a un usuario del tenant (TenantUser): una persona que atiende.</summary>
    Usuario = 1
}

/// <summary>Estados de un turno (ADR-0165 ola 4). Se persiste como texto.</summary>
public enum AgendaTurnoEstado
{
    /// <summary>Tomado, sin confirmar.</summary>
    Reservado = 0,

    /// <summary>El cliente confirmo que asiste.</summary>
    Confirmado = 1,

    /// <summary>Se presto el servicio.</summary>
    Atendido = 2,

    /// <summary>El cliente no llego. Sigue ocupando el cupo (ya paso).</summary>
    NoAsistio = 3,

    /// <summary>Cancelado: libera el cupo.</summary>
    Cancelado = 4
}

/// <summary>Por donde entro el turno (ADR-0165 ola 4). Se persiste como texto.</summary>
public enum AgendaTurnoOrigen
{
    Manual = 0,
    Agente = 1,
    Portal = 2
}

/// <summary>
/// Tipo de novedad que cierra (total o parcialmente) la agenda de un recurso en un rango de fechas
/// (ADR-0165). Equivalente por-recurso del dia no operativo del calendario del tenant. Se persiste como texto.
/// </summary>
public enum AgendaNovedadTipo
{
    Vacaciones = 0,
    Incapacidad = 1,
    Permiso = 2,
    Capacitacion = 3,
    Mantenimiento = 4,
    Otro = 5
}
