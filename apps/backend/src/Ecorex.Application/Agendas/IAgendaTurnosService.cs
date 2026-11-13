using Ecorex.Domain.Enums;

namespace Ecorex.Application.Agendas;

public sealed record AgendaRequisitoDto(string Nombre, IReadOnlyList<Guid> RecursoIds);

public sealed record AgendaServicioDto(
    Guid Id, string Nombre, string? Descripcion, int DuracionMinutos, int AnticipacionMinMinutos, int AnticipacionMaxDias,
    bool AgentePuedeReservar, bool Activo, long Version, IReadOnlyList<AgendaRequisitoDto> Requisitos, int TurnosProximos);

public sealed record SaveAgendaServicioRequest(
    Guid? Id, string Nombre, string? Descripcion, int DuracionMinutos, int AnticipacionMinMinutos, int AnticipacionMaxDias,
    bool AgentePuedeReservar, bool Activo, IReadOnlyList<AgendaRequisitoDto> Requisitos, long? Version = null);

public sealed record AgendaTurnoRecursoDto(string Requisito, Guid RecursoId, string RecursoNombre);

public sealed record AgendaTurnoDto(
    Guid Id, Guid ServicioId, string ServicioNombre, DateOnly Fecha, TimeOnly HoraInicio, TimeOnly HoraFin,
    AgendaTurnoEstado Estado, AgendaTurnoOrigen Origen, string ClienteNombre, string? ClienteTelefono, string? Nota,
    int Reprogramaciones, string? Historial, long Version, IReadOnlyList<AgendaTurnoRecursoDto> Recursos,
    string? Conflicto = null);

public sealed record ReservarTurnoRequest(
    Guid ServicioId, DateOnly Fecha, TimeOnly Inicio, string ClienteNombre, string? ClienteTelefono, string? Nota,
    AgendaTurnoOrigen Origen = AgendaTurnoOrigen.Manual);

public sealed record ReprogramarTurnoRequest(Guid TurnoId, DateOnly Fecha, TimeOnly Inicio, string? Motivo, long? Version = null);

public sealed record CambiarEstadoTurnoRequest(Guid TurnoId, AgendaTurnoEstado Estado, string? Motivo, long? Version = null);

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
    Task<IReadOnlyList<AgendaHueco>> HuecosAsync(Guid servicioId, DateOnly desde, int dias, Guid? excluirTurnoId = null, CancellationToken cancellationToken = default);

    Task<AgendaOpResult> ReservarAsync(ReservarTurnoRequest request, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> ReprogramarAsync(ReprogramarTurnoRequest request, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> CambiarEstadoAsync(CambiarEstadoTurnoRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgendaTurnoDto>> ListTurnosAsync(DateOnly desde, DateOnly hasta, Guid? recursoId = null, Guid? servicioId = null, CancellationToken cancellationToken = default);
    Task<AgendaTurnoDto?> GetTurnoAsync(Guid id, CancellationToken cancellationToken = default);
}
