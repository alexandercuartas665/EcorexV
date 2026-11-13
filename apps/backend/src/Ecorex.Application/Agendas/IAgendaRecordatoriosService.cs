using Ecorex.Domain.Enums;

namespace Ecorex.Application.Agendas;

public sealed record AgendaRecordatorioReglaDto(
    Guid Id, string Nombre, bool Activa, IReadOnlyList<Guid> ServicioIds, int AnticipacionMinutos,
    TimeOnly? HoraDesde, TimeOnly? HoraHasta, AgendaRecordatorioFueraHorario FueraDeHorario, bool SoloSiNoConfirmado,
    AgendaRecordatorioCanal Canal, Guid? WhatsAppLineId, Guid? PlantillaId, IReadOnlyDictionary<string, string> Variables,
    Guid? AiAgentId, Guid? VoiceLineId, string? Objetivo, long Version);

public sealed record SaveAgendaRecordatorioReglaRequest(
    Guid? Id, string Nombre, bool Activa, IReadOnlyList<Guid> ServicioIds, int AnticipacionMinutos,
    TimeOnly? HoraDesde, TimeOnly? HoraHasta, AgendaRecordatorioFueraHorario FueraDeHorario, bool SoloSiNoConfirmado,
    AgendaRecordatorioCanal Canal, Guid? WhatsAppLineId, Guid? PlantillaId, IReadOnlyDictionary<string, string> Variables,
    Guid? AiAgentId = null, Guid? VoiceLineId = null, string? Objetivo = null, long? Version = null);

public sealed record AgendaRecordatorioLineaDto(Guid Id, string Nombre, string Proveedor, bool Conectada);

/// <summary>Linea de voz (Retell) para el recordatorio por llamada. Lista = habilitada, con API key y numero saliente.
/// ModoSimple = la linea usa un agente ya armado en Retell (debe tener los campos de analisis resultado_turno y nota_turno).</summary>
public sealed record AgendaRecordatorioLineaVozDto(Guid Id, string Nombre, bool Lista, bool ModoSimple);

public sealed record AgendaRecordatorioPlantillaDto(
    Guid Id, Guid LineaId, string Nombre, string Idioma, string Estado, bool Usable, IReadOnlyList<string> Variables, string Cuerpo);

public sealed record AgendaRecordatorioOpcionesDto(
    IReadOnlyList<AgendaRecordatorioLineaDto> Lineas,
    IReadOnlyList<AgendaRecordatorioPlantillaDto> Plantillas,
    IReadOnlyList<AgendaOpcionDto> Servicios,
    IReadOnlyList<AgendaOpcionDto> Agentes,
    IReadOnlyList<AgendaRecordatorioLineaVozDto> LineasVoz);

public sealed record AgendaRecordatorioEnvioDto(
    Guid Id, Guid ReglaId, string ReglaNombre, Guid TurnoId, string Cliente, string Servicio, DateOnly FechaTurno,
    TimeOnly HoraTurno, AgendaRecordatorioCanal Canal, DateTimeOffset ProgramadoUtc, DateTimeOffset ProcesadoUtc,
    AgendaRecordatorioEnvioEstado Estado, string? Detalle);

/// <summary>Resultado de una pasada de recordatorios.</summary>
/// <param name="LlamadasResueltas">Llamadas de recordatorio cuyo resultado se aplico al turno en esta pasada.</param>
public sealed record AgendaRecordatorioCorrida(int Enviados, int Fallidos, int Omitidos, IReadOnlyList<string> Detalle, int LlamadasResueltas = 0);

/// <summary>
/// Recordatorios de turnos (ADR-0167): reglas del tenant (cuando, horario permitido, canal, linea, plantilla,
/// variables) y el procesamiento que decide y envia. Etapa 1 = WhatsApp con plantilla; etapa 2 = llamada con agente de
/// voz (Retell) cuyo resultado (confirma / cancela / reprogramar / no contesto) se aplica al turno.
/// </summary>
public interface IAgendaRecordatoriosService
{
    Task<IReadOnlyList<AgendaRecordatorioReglaDto>> ListReglasAsync(CancellationToken cancellationToken = default);
    Task<AgendaOpResult> SaveReglaAsync(SaveAgendaRecordatorioReglaRequest request, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> DeleteReglaAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AgendaRecordatorioOpcionesDto> OpcionesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgendaRecordatorioEnvioDto>> ListEnviosAsync(int maximo = 100, Guid? turnoId = null, CancellationToken cancellationToken = default);

    /// <summary>Procesa los recordatorios que ya tocan para el tenant ambiente (lo llama el worker y el boton
    /// "Procesar ahora").</summary>
    Task<AgendaRecordatorioCorrida> ProcesarAsync(CancellationToken cancellationToken = default);

    /// <summary>Tenants con al menos una regla activa o una llamada de recordatorio esperando resultado (barrido del
    /// worker; solo ids).</summary>
    Task<IReadOnlyList<Guid>> TenantsConReglasAsync(CancellationToken cancellationToken = default);
}
