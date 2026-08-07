using Ecorex.Domain.Enums;

namespace Ecorex.Application.Reporting.External;

/// <summary>
/// Administracion del catalogo de fuentes externas gobernadas (ADR-0064). CRUD de
/// <see cref="Ecorex.Domain.Entities.ExternalDataSource"/> / <see cref="Ecorex.Domain.Entities.ExternalDataSet"/>
/// y concesiones por tenant. Cross-tenant por naturaleza, por eso queda restringido a PlatformAdmin y
/// AUDITADO (SuperAdminAuditLog dentro de la transaccion), como el resto de acciones de plataforma. El
/// secreto (cadena de conexion) se cifra y NUNCA se devuelve ni se loggea en claro.
/// </summary>
public interface IExternalDataSourceService
{
    Task<IReadOnlyList<ExternalDataSourceSummary>> ListSourcesAsync(CancellationToken ct = default);
    Task<ExternalDataSourceDetail?> GetSourceAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CreateSourceAsync(SaveExternalDataSourceRequest request, Guid actorUserId, CancellationToken ct = default);
    Task<bool> UpdateSourceAsync(Guid id, SaveExternalDataSourceRequest request, Guid actorUserId, CancellationToken ct = default);
    Task<bool> SetSourceEnabledAsync(Guid id, bool enabled, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Prueba de conexion de SOLO LECTURA. Usa la cadena del request si llega; si viene vacia,
    /// la ya guardada (descifrada solo en memoria). Devuelve null si OK, o el error legible.</summary>
    Task<string?> TestConnectionAsync(Guid id, SaveExternalDataSourceRequest? request, Guid actorUserId, CancellationToken ct = default);

    Task<IReadOnlyList<ExternalDataSetSummary>> ListDataSetsAsync(Guid sourceId, CancellationToken ct = default);
    Task<ExternalDataSetDetail?> GetDataSetAsync(Guid id, CancellationToken ct = default);
    Task<Guid> SaveDataSetAsync(SaveExternalDataSetRequest request, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteDataSetAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    Task<IReadOnlyList<ExternalGrantDto>> ListGrantsAsync(Guid sourceId, CancellationToken ct = default);
    Task<Guid> GrantAsync(Guid sourceId, Guid tenantId, Guid? rolId, Guid actorUserId, CancellationToken ct = default);
    Task<bool> RevokeAsync(Guid grantId, Guid actorUserId, CancellationToken ct = default);
}

public sealed record ExternalDataSourceSummary(
    Guid Id, string Name, ExternalDataProvider Provider, bool IsEnabled, bool HasConnectionString,
    DateTimeOffset? LastValidatedAt, int DataSetCount, int GrantCount);

public sealed record ExternalDataSourceDetail(
    Guid Id, string Name, string? Description, ExternalDataProvider Provider, bool HasConnectionString,
    bool IsReadOnly, bool IsEnabled, DateTimeOffset? LastValidatedAt);

public sealed record SaveExternalDataSourceRequest(
    string Name, string? Description, ExternalDataProvider Provider, string? ConnectionString, bool IsEnabled);

public sealed record ExternalDataSetSummary(
    Guid Id, Guid ExternalDataSourceId, string Name, bool IsEnabled, int ParameterCount, int FieldCount);

public sealed record ExternalDataSetDetail(
    Guid Id, Guid ExternalDataSourceId, string Name, string? Description, string CommandText,
    IReadOnlyList<ExternalDataSetParameter> Parameters, IReadOnlyList<ExternalDataSetField> Fields, bool IsEnabled);

public sealed record SaveExternalDataSetRequest(
    Guid? Id, Guid ExternalDataSourceId, string Name, string? Description, string CommandText,
    IReadOnlyList<ExternalDataSetParameter> Parameters, IReadOnlyList<ExternalDataSetField> Fields, bool IsEnabled);

public sealed record ExternalGrantDto(
    Guid Id, Guid ExternalDataSourceId, Guid TenantId, string? TenantName, Guid? RolId, bool IsEnabled);
