using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy.DataConnections;

// Conexiones de datos externas PROPIAS del tenant (extension tenant-scoped de ADR-0064). Cada empresa
// gestiona SOLO las suyas (OwnerTenantId). El secreto se cifra con ISecretProtector y nunca se devuelve
// en claro. La escritura se habilita por conexion (AllowWrite); por defecto es solo lectura.

/// <summary>Fila del listado de conexiones del tenant.</summary>
public sealed record TenantDataConnectionSummary(
    Guid Id, string Name, ExternalDataProvider Provider, bool IsEnabled, bool HasConnectionString,
    bool AllowWrite, DateTimeOffset? LastValidatedAt, int DatasetCount);

/// <summary>Detalle de una conexion (sin la cadena en claro; solo si HAY cadena).</summary>
public sealed record TenantDataConnectionDetail(
    Guid Id, string Name, string? Description, ExternalDataProvider Provider, bool HasConnectionString,
    bool IsEnabled, bool AllowWrite, DateTimeOffset? LastValidatedAt);

/// <summary>Alta/edicion. ConnectionString vacio en edicion = conservar la actual.</summary>
public sealed record SaveTenantDataConnectionRequest(
    string Name, string? Description, ExternalDataProvider Provider, string? ConnectionString,
    bool AllowWrite, bool IsEnabled);

public sealed record TenantDatasetSummary(Guid Id, string Name, string? Description, bool IsEnabled);

public sealed record TenantDatasetDetail(
    Guid Id, Guid ConnectionId, string Name, string? Description, string CommandText, bool IsEnabled);

public sealed record SaveTenantDatasetRequest(
    Guid? Id, Guid ConnectionId, string Name, string? Description, string CommandText, bool IsEnabled);

/// <summary>Resultado tabular de una consulta (columnas + filas como texto para pintar la grilla).</summary>
public sealed record ExternalQueryGrid(
    IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows, int RowCount, bool Truncated);

/// <summary>Envoltura de ejecucion: Ok con grilla, o error legible (conexion/SQL/solo-lectura).</summary>
public sealed record TenantQueryResult(bool Ok, ExternalQueryGrid? Grid, string? Error);
