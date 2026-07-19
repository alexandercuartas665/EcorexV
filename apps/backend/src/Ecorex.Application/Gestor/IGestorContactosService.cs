using Ecorex.Application.Directorio;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Gestor;

/// <summary>
/// Gestor de Clientes (modulo 000740): prospectos scrapeados, Bolsa de contactos (kanban de
/// terceros por columna), oportunidades (embudo), agenda de citas y filtros dinamicos guardados.
/// Tenant-scoped (filtro global + estampado en alta); aqui NUNCA se filtra a mano por TenantId.
/// Reutiliza el resultado tipado del Directorio (<see cref="TerceroResult{T}"/>).
/// </summary>
public interface IGestorContactosService
{
    // ---- KPIs ----
    Task<GestorKpisDto> GetKpisAsync(CancellationToken cancellationToken = default);

    // ---- Prospectos scrapeados ----
    Task<IReadOnlyList<ProspectoDto>> ListProspectosAsync(
        string? fuente, CancellationToken cancellationToken = default);

    /// <summary>Promueve un prospecto a Tercero (Persona/Sospechoso/Prospecto) y lo mete en la
    /// primera columna de la Bolsa. Devuelve el Id del tercero creado.</summary>
    Task<TerceroResult<Guid>> PromoverProspectoAsync(
        Guid prospectoId, CancellationToken cancellationToken = default);

    // ---- Bolsa (kanban de terceros por columna) ----
    Task<IReadOnlyList<BolsaColumnaDto>> ListColumnasAsync(CancellationToken cancellationToken = default);

    Task<TerceroResult<BolsaColumnaDto>> SaveColumnaAsync(
        Guid? id, string nombre, string color, CancellationToken cancellationToken = default);

    Task<TerceroResult<bool>> DeleteColumnaAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BolsaTerceroDto>> ListBolsaAsync(CancellationToken cancellationToken = default);

    Task<TerceroResult<bool>> MoverTerceroAsync(
        Guid terceroId, Guid columnaId, CancellationToken cancellationToken = default);

    // ---- Oportunidades ----
    Task<IReadOnlyList<OportunidadDto>> ListOportunidadesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OportunidadDto>> ListOportunidadesByTerceroAsync(
        Guid terceroId, CancellationToken cancellationToken = default);

    Task<TerceroResult<OportunidadDto>> CreateOportunidadAsync(
        Guid terceroId, SaveOportunidadRequest req, CancellationToken cancellationToken = default);

    Task<TerceroResult<OportunidadDto>> UpdateOportunidadAsync(
        Guid id, SaveOportunidadRequest req, CancellationToken cancellationToken = default);

    Task<TerceroResult<bool>> MoverEtapaAsync(
        Guid id, OportunidadEtapa etapa, CancellationToken cancellationToken = default);

    /// <summary>Mueve la oportunidad a una etapa CONFIGURABLE del pipeline (000740) por FK.</summary>
    Task<TerceroResult<bool>> MoverEstadoAsync(
        Guid id, Guid estadoId, CancellationToken cancellationToken = default);

    Task<TerceroResult<bool>> DeleteOportunidadAsync(Guid id, CancellationToken cancellationToken = default);

    // ---- Citas / Agenda ----
    Task<IReadOnlyList<CitaDto>> ListCitasAsync(int anio, int mes, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CitaDto>> ListCitasByTerceroAsync(
        Guid terceroId, CancellationToken cancellationToken = default);

    Task<TerceroResult<CitaDto>> CreateCitaAsync(SaveCitaRequest req, CancellationToken cancellationToken = default);

    Task<TerceroResult<CitaDto>> UpdateCitaAsync(
        Guid id, SaveCitaRequest req, CancellationToken cancellationToken = default);

    Task<TerceroResult<bool>> DeleteCitaAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TerceroResult<bool>> SetCompletadaAsync(
        Guid id, bool completada, CancellationToken cancellationToken = default);

    // ---- Filtros dinamicos ----
    Task<IReadOnlyList<FiltroDto>> ListFiltrosAsync(CancellationToken cancellationToken = default);

    Task<TerceroResult<FiltroDto>> SaveFiltroAsync(
        Guid? id, SaveFiltroRequest req, CancellationToken cancellationToken = default);

    Task<TerceroResult<bool>> DeleteFiltroAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Conteo en vivo de terceros que cumplen los criterios (segmento).</summary>
    Task<int> ContarAsync(
        IReadOnlyList<FiltroCriterio> criterios, string? fuente, CancellationToken cancellationToken = default);
}
