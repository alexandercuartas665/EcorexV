using Ecorex.Domain.Enums;

namespace Ecorex.Application.Crm;

/// <summary>Concepto de actividad del CRM (000125) para la lista y el detalle.</summary>
public sealed record ConceptoActividadDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? FormDefinitionId,
    string? FormTitle,
    string? FormCode,
    bool HandlesValues,
    ConceptoActividadMode Mode,
    bool IsArchived,
    int SortOrder);

/// <summary>Alta o edicion de un concepto de actividad.</summary>
public sealed record SaveConceptoActividadRequest(
    string Code,
    string Name,
    string? Description,
    Guid? FormDefinitionId,
    bool HandlesValues,
    ConceptoActividadMode Mode);

/// <summary>Resultado con error legible para la UI (p.ej. codigo duplicado).</summary>
public sealed record ConceptoResult<T>(bool Ok, T? Value, string? Error)
{
    public static ConceptoResult<T> Success(T value) => new(true, value, null);
    public static ConceptoResult<T> Fail(string error) => new(false, default, error);
}

public interface IConceptoActividadService
{
    Task<IReadOnlyList<ConceptoActividadDto>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<ConceptoActividadDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ConceptoResult<ConceptoActividadDto>> CreateAsync(SaveConceptoActividadRequest request, CancellationToken cancellationToken = default);
    Task<ConceptoResult<ConceptoActividadDto>> UpdateAsync(Guid id, SaveConceptoActividadRequest request, CancellationToken cancellationToken = default);
    Task<ConceptoResult<bool>> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);
}
