using Ecorex.Domain.Enums;

namespace Ecorex.Application.Crm;

/// <summary>Etapa configurable del pipeline de oportunidades del CRM (000740) para la lista y el pipeline.</summary>
public sealed record OportunidadEstadoDto(
    Guid Id,
    string Name,
    int SortOrder,
    string Color,
    OportunidadEstadoTipo Tipo,
    bool IsArchived);

/// <summary>Alta o edicion de una etapa configurable del pipeline.</summary>
public sealed record SaveOportunidadEstadoRequest(
    string Name,
    string Color,
    OportunidadEstadoTipo Tipo);

/// <summary>
/// Catalogo de etapas CONFIGURABLES del pipeline de oportunidades (000740). Cada tenant define
/// las suyas (nombre, orden, color, tipo Abierta/Ganada/Perdida). Tenant-scoped por el filtro
/// global. Reemplaza al enum fijo OportunidadEtapa (que se conserva como respaldo/backfill).
/// Reutiliza el resultado tipado <see cref="ConceptoResult{T}"/> del CRM.
/// </summary>
public interface IOportunidadEstadoService
{
    Task<IReadOnlyList<OportunidadEstadoDto>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<OportunidadEstadoDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ConceptoResult<OportunidadEstadoDto>> CreateAsync(SaveOportunidadEstadoRequest request, CancellationToken cancellationToken = default);
    Task<ConceptoResult<OportunidadEstadoDto>> UpdateAsync(Guid id, SaveOportunidadEstadoRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reordena el pipeline: aplica SortOrder segun la posicion en la lista de ids.</summary>
    Task<ConceptoResult<bool>> ReorderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default);

    /// <summary>Archiva/restaura (soft). Nunca hard-delete; conserva las oportunidades historicas.</summary>
    Task<ConceptoResult<bool>> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotente: si el tenant actual no tiene ninguna etapa, siembra las 6 por defecto que
    /// mapean el enum heredado (Nueva/Calificada/Propuesta/Negociacion/Ganada/Perdida).
    /// </summary>
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rellena EstadoId en las oportunidades del tenant que aun lo tienen null, casando por
    /// SortOrder == (int)o.Etapa. Idempotente (solo toca las de EstadoId null).
    /// </summary>
    Task BackfillAsync(CancellationToken cancellationToken = default);
}
