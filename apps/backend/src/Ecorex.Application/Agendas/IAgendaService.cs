namespace Ecorex.Application.Agendas;

/// <summary>
/// Modulo de Agendas (ADR-0165, olas 1 y 2): tipos de recurso, recursos agendables, agendas base (plantillas
/// semanales), agenda propia del recurso por vigencia (importada de una plantilla y editable), novedades y la
/// disponibilidad semanal resultante. Todo tenant-scoped por el filtro global; borrados logicos.
/// </summary>
public interface IAgendaService
{
    // ---- Tipos de recurso ----
    Task<IReadOnlyList<AgendaTipoDto>> ListTiposAsync(CancellationToken cancellationToken = default);
    Task<AgendaOpResult> SaveTipoAsync(SaveAgendaTipoRequest request, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> DeleteTipoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Crea los tipos sugeridos (Persona, Grupo, Espacio, Equipo, Vehiculo, Punto de atencion) que falten,
    /// por nombre. Idempotente. Devuelve cuantos creo.</summary>
    Task<int> CrearTiposSugeridosAsync(CancellationToken cancellationToken = default);

    // ---- Recursos ----
    Task<IReadOnlyList<AgendaRecursoDto>> ListRecursosAsync(CancellationToken cancellationToken = default);
    Task<AgendaRecursoDto?> GetRecursoAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> SaveRecursoAsync(SaveAgendaRecursoRequest request, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> DeleteRecursoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Sedes activas del tenant (Entidad tipo Sede) y usuarios activos, para los selectores.</summary>
    Task<IReadOnlyList<AgendaOpcionDto>> ListSedesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgendaOpcionDto>> ListUsuariosAsync(CancellationToken cancellationToken = default);

    // ---- Agendas base (plantillas) ----
    Task<IReadOnlyList<AgendaPlantillaDto>> ListPlantillasAsync(CancellationToken cancellationToken = default);
    Task<AgendaOpResult> SavePlantillaAsync(SaveAgendaPlantillaRequest request, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> DuplicarPlantillaAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> DeletePlantillaAsync(Guid id, CancellationToken cancellationToken = default);

    // ---- Agenda del recurso (vigencias) ----
    Task<IReadOnlyList<AgendaVigenciaDto>> ListVigenciasAsync(Guid recursoId, CancellationToken cancellationToken = default);

    /// <summary>Copia las franjas de la plantilla en una vigencia NUEVA del recurso. La copia queda independiente.</summary>
    Task<AgendaOpResult> ImportarPlantillaAsync(ImportarPlantillaRequest request, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> SaveVigenciaAsync(SaveVigenciaRequest request, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> DeleteVigenciaAsync(Guid id, CancellationToken cancellationToken = default);

    // ---- Novedades ----
    Task<IReadOnlyList<AgendaNovedadDto>> ListNovedadesAsync(Guid recursoId, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> SaveNovedadAsync(SaveAgendaNovedadRequest request, CancellationToken cancellationToken = default);
    Task<AgendaOpResult> DeleteNovedadAsync(Guid id, CancellationToken cancellationToken = default);

    // ---- Disponibilidad ----
    /// <summary>Fecha de hoy en la zona horaria del tenant (regla 9).</summary>
    Task<DateOnly> HoyAsync(CancellationToken cancellationToken = default);

    /// <summary>Los 7 dias (lunes a domingo) de la semana que contiene <paramref name="fecha"/>.</summary>
    Task<IReadOnlyList<AgendaDiaDto>> SemanaAsync(Guid recursoId, DateOnly fecha, CancellationToken cancellationToken = default);
}
