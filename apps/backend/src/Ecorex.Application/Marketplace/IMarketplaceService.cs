using Ecorex.Domain.Enums;

namespace Ecorex.Application.Marketplace;

/// <summary>Resultado simple del marketplace (Ok/valor/error legible).</summary>
public sealed record MarketplaceResult<T>(bool Ok, T? Value, string? Error)
{
    public static MarketplaceResult<T> Success(T value) => new(true, value, null);
    public static MarketplaceResult<T> Fail(string error) => new(false, default, error);
}

/// <summary>Item del marketplace para la galeria/admin (sin el snapshot pesado).</summary>
public sealed record MarketplaceItemDto(
    Guid Id, MarketplaceItemKind Kind, string Title, string? Description, string? ImageRef,
    string? Category, string? SourceCode, bool IsActive, int ImportCount, DateTimeOffset PublishedAt);

/// <summary>Detalle con el snapshot portable (para "Traer" en la Ola B3).</summary>
public sealed record MarketplaceItemDetailDto(
    Guid Id, MarketplaceItemKind Kind, string Title, string? Description, string? ImageRef,
    string? Category, string? SourceCode, bool IsActive, int ImportCount, DateTimeOffset PublishedAt,
    string SnapshotJson, int SnapshotFormatVersion);

/// <summary>Datos editables al publicar/editar un item.</summary>
public sealed record MarketplacePublishInput(
    string Title, string? Description, string? Category, string? ImageRef);

/// <summary>
/// Catalogo del MARKETPLACE de plantillas (ADR-0097 Ola B2). El PlatformAdmin PUBLICA un flujo o formulario
/// existente (con su snapshot portable + imagen/descripcion); todos los tenants lo LEEN. El "Traer" (import
/// a un tenant) es la Ola B3. El servicio no impone auth: el gate PlatformAdmin va en la UI/endpoint.
/// </summary>
public interface IMarketplaceService
{
    Task<MarketplaceResult<MarketplaceItemDto>> PublishFlowAsync(Guid definitionId, MarketplacePublishInput input, Guid? platformUserId, CancellationToken cancellationToken = default);
    Task<MarketplaceResult<MarketplaceItemDto>> PublishFormAsync(Guid definitionId, MarketplacePublishInput input, Guid? platformUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketplaceItemDto>> ListAsync(MarketplaceItemKind? kind, string? category, string? query, bool includeInactive, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken cancellationToken = default);
    Task<MarketplaceItemDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MarketplaceResult<MarketplaceItemDto>> UpdateAsync(Guid id, MarketplacePublishInput input, bool isActive, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
