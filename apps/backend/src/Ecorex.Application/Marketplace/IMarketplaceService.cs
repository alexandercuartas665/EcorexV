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

/// <summary>Vista previa para el asistente de "Traer" (Ola B3): para un flujo dice si trae formularios
/// vinculados a los nodos, para preguntarle al usuario si desea migrarlos.</summary>
public sealed record MarketplaceImportPreviewDto(
    Guid Id, MarketplaceItemKind Kind, string Title, bool HasNodeForms, int NodeFormsCount);

/// <summary>Reporte del "Traer" (import a un tenant) de un item del marketplace (Ola B3). Para un flujo
/// incluye lo que quedo SIN mapear (cargos/agentes/reglas que no existen en el tenant destino) para que
/// el usuario lo cablee antes de publicar el flujo importado.</summary>
public sealed record MarketplaceImportResult(
    MarketplaceItemKind Kind,
    Guid NewDefinitionId,
    string NewCode,
    string NewTitle,
    int FormsImported,
    IReadOnlyList<string> UnmappedCargos,
    IReadOnlyList<string> UnmappedAgents,
    IReadOnlyList<string> UnmappedRules,
    IReadOnlyList<string> Warnings);

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

    // ---- Ola B3: "Traer" al tenant activo ----

    /// <summary>Vista previa para el asistente: si el item es un flujo con formularios en sus nodos (para
    /// preguntar si migrarlos). Null si el item no existe o no esta activo.</summary>
    Task<MarketplaceImportPreviewDto?> GetImportPreviewAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Importa el item al TENANT ACTIVO (crea un flujo/formulario BORRADOR nuevo) y suma 1 a
    /// ImportCount. <paramref name="includeNodeForms"/> solo aplica a flujos (migrar o no los formularios
    /// vinculados a los nodos).</summary>
    Task<MarketplaceResult<MarketplaceImportResult>> ImportAsync(Guid id, bool includeNodeForms, CancellationToken cancellationToken = default);
}
