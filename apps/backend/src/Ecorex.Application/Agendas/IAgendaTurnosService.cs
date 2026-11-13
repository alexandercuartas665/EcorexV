using Ecorex.Domain.Enums;

namespace Ecorex.Application.Agendas;

public sealed record AgendaRequisitoDto(string Nombre, IReadOnlyList<Guid> RecursoIds);

public sealed record AgendaServicioDto(
    Guid Id, string Nombre, string? Descripcion, int DuracionMinutos, int AnticipacionMinMinutos, int AnticipacionMaxDias,
    bool AgentePuedeReservar, bool Activo, long Version, IReadOnlyList<AgendaRequisitoDto> Requisitos, int TurnosProximos,
    Guid? ConceptoId = null, string? ConceptoNombre = null);

public sealed record SaveAgendaServicioRequest(
    Guid? Id, string Nombre, string? Descripcion, int DuracionMinutos, int AnticipacionMinMinutos, int AnticipacionMaxDias,
    bool AgentePuedeReservar, bool Activo, IReadOnlyList<AgendaRequisitoDto> Requisitos, long? Version = null,
    Guid? ConceptoId = null);

public sealed record AgendaTurnoRecursoDto(string Requisito, Guid RecursoId, string RecursoNombre);

public sealed record AgendaTurnoDto(
    Guid Id, Guid ServicioId, string ServicioNombre, DateOnly Fecha, TimeOnly HoraInicio, TimeOnly HoraFin,
    AgendaTurnoEstado Estado, AgendaTurnoOrigen Origen, string ClienteNombre, string? ClienteTelefono, string? Nota,
    int Reprogramaciones, string? Historial, long Version, IReadOnlyList<AgendaTurnoRecursoDto> Recursos,
    string? Conflicto = null, Guid? TerceroId = null, string? TerceroNombre = null, Guid? TaskItemId = null,
    string? TaskNumero = null);

/// <summary>
/// Pedido de reserva. <paramref name="TerceroId"/> liga el turno a un contacto del Directorio (si falta el telefono se
/// toma de su ficha). <paramref name="SoloRecursosAgente"/> limita los recursos a los que tienen "el agente puede
/// agendar" (lo usan las herramientas del agente). <paramref name="ActorName"/> firma la tarea que nace del turno.
/// </summary>
public sealed record ReservarTurnoRequest(
    Guid ServicioId, DateOnly Fecha, TimeOnly Inicio, string ClienteNombre, string? ClienteTelefono, string? Nota,
    AgendaTurnoOrigen Origen = AgendaTurnoOrigen.Manual, Guid? TerceroId = null, Guid? ConversationId = null,
    bool SoloRecursosAgente = false, string? ActorName = null);

public sealed record ReprogramarTurnoRequest(Guid TurnoId, DateOnly Fecha, TimeOnly Inicio, string? Motivo, long? Version = null,
    bool SoloRecursosAgente = false, string? ActorName = null);

public sealed record CambiarEstadoTurnoRequest(Guid TurnoId, AgendaTurnoEstado Estado, string? Motivo, long? Version = null,
    string? ActorName = null);

/// <summary>
/// Agendas olas 3 y 4 (ADR-0165): servicios agendables (duracion, anticipacion, recursos que ocupa a la vez) y
/// turnos (reservar, reprogramar, confirmar, atender, no asistio, cancelar). Los huecos salen de la agenda de cada
/// recurso - calendario de la entidad (festivos/dias no operativos) - novedades - turnos ya tomados.
/// </summary>
public interface IAgendaTurnosService
{
    Task<IReadOnlyList<AgendaServicioDto>> ListServiciosAsync(CancellationToken cancellationToken = default);
    Task<AgendaOpResult> SaveServicioAsync(SaveAgendaServicioRequest request, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> DeleteServicioAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Huecos libres del servicio desde <paramref name="desde"/> durante <paramref name="dias"/> dias (max 31).
    /// <paramref name="excluirTurnoId"/> no cuenta la ocupacion de ese turno (para reprogramarlo).</summary>
    Task<IReadOnlyList<AgendaHueco>> HuecosAsync(Guid servicioId, DateOnly desde, int dias, Guid? excluirTurnoId = null,
        bool soloRecursosAgente = false, CancellationToken cancellationToken = default);

    Task<AgendaOpResult> ReservarAsync(ReservarTurnoRequest request, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> ReprogramarAsync(ReprogramarTurnoRequest request, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> CambiarEstadoAsync(CambiarEstadoTurnoRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgendaTurnoDto>> ListTurnosAsync(DateOnly desde, DateOnly hasta, Guid? recursoId = null, Guid? servicioId = null, CancellationToken cancellationToken = default);
    Task<AgendaTurnoDto?> GetTurnoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Turnos de un cliente desde <paramref name="desde"/>: los de su contacto del Directorio, los de la
    /// conversacion o los de su telefono (ultimos 10 digitos). Para el agente y la ficha del contacto.</summary>
    Task<IReadOnlyList<AgendaTurnoDto>> TurnosDelClienteAsync(Guid? terceroId, string? telefono, Guid? conversationId, DateOnly desde,
        CancellationToken cancellationToken = default);

    /// <summary>Conceptos (tipos de actividad) activos del tenant, para elegir la tarea que nace con cada turno.</summary>
    Task<IReadOnlyList<AgendaOpcionDto>> ListConceptosAsync(CancellationToken cancellationToken = default);

    /// <summary>Fecha de hoy en la zona horaria del tenant.</summary>
    Task<DateOnly> HoyAsync(CancellationToken cancellationToken = default);
}
