using Ecorex.Domain.Enums;

namespace Ecorex.Application.Cards;

/// <summary>Etiqueta de tarjeta (ADR-0097 Fase A) para la UI.</summary>
public sealed record CardTagDto(Guid Id, string Name, string? Color, int SortOrder);

/// <summary>
/// Gestion de etiquetas (categorias) de las tarjetas de flujos y formularios, y su asignacion por
/// tarjeta (ADR-0097 Fase A). Tenant-scoped por el filtro global. El "codigo de tarjeta" es la
/// identidad estable: ProcessCode para flujos, FormCode para formularios.
/// </summary>
public interface ICardTagService
{
    // ---- Catalogo de etiquetas por ambito ----
    Task<IReadOnlyList<CardTagDto>> ListTagsAsync(CardTagScope scope, CancellationToken cancellationToken = default);
    Task<CardTagDto?> CreateTagAsync(CardTagScope scope, string name, string? color, CancellationToken cancellationToken = default);
    Task<bool> UpdateTagAsync(Guid tagId, string name, string? color, CancellationToken cancellationToken = default);
    /// <summary>Borra la etiqueta y todos sus enlaces (no toca los flujos/formularios).</summary>
    Task<bool> DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default);
    Task ReorderTagsAsync(CardTagScope scope, IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default);

    // ---- Asignacion por tarjeta ----
    /// <summary>Mapa cardCode -> etiquetas asignadas, para TODAS las tarjetas del tenant en ese ambito
    /// (lo consume la vista agrupada).</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<Guid>>> GetAssignmentsAsync(CardTagScope scope, CancellationToken cancellationToken = default);
    /// <summary>Reemplaza el conjunto de etiquetas de UNA tarjeta.</summary>
    Task SetCardTagsAsync(CardTagScope scope, string cardCode, IReadOnlyList<Guid> tagIds, CancellationToken cancellationToken = default);

    /// <summary>Backfill idempotente: crea etiquetas a partir de los WorkflowDefinition.Category
    /// existentes y las asigna, para no perder el agrupado actual al estrenar el sistema de etiquetas.</summary>
    Task BackfillFlowCategoriesAsync(CancellationToken cancellationToken = default);
}
