namespace Ecorex.Application.Tenancy.DataConnections;

/// <summary>
/// Conexiones de datos externas PROPIAS del tenant activo (extension tenant-scoped de ADR-0064). TODAS las
/// operaciones se acotan al tenant actual (ITenantContext): una empresa nunca ve ni ejecuta las conexiones
/// de otra. El secreto se cifra (ISecretProtector); la escritura se gobierna por conexion (AllowWrite).
/// </summary>
public interface ITenantDataConnectionService
{
    Task<IReadOnlyList<TenantDataConnectionSummary>> ListAsync(CancellationToken ct = default);
    Task<TenantDataConnectionDetail?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Guid?> SaveAsync(Guid? id, SaveTenantDataConnectionRequest request, Guid actorUserId, CancellationToken ct = default);
    Task<bool> SetEnabledAsync(Guid id, bool enabled, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Prueba de conexion (SELECT 1). Devuelve null si OK, o el mensaje de error.</summary>
    Task<string?> TestConnectionAsync(Guid id, SaveTenantDataConnectionRequest? request, Guid actorUserId, CancellationToken ct = default);

    Task<IReadOnlyList<TenantDatasetSummary>> ListDatasetsAsync(Guid connectionId, CancellationToken ct = default);
    Task<TenantDatasetDetail?> GetDatasetAsync(Guid id, CancellationToken ct = default);
    Task<Guid?> SaveDatasetAsync(SaveTenantDatasetRequest request, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteDatasetAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Ejecuta SQL ad-hoc ("conexion directa") contra la conexion. Respeta AllowWrite de la conexion.</summary>
    Task<TenantQueryResult> RunQueryAsync(Guid connectionId, string sql, int maxRows, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Ejecuta el CommandText de un dataset guardado, enlazando sus parametros con los valores
    /// provistos (inputs por Name; los que falten usan el DefaultValue declarado).</summary>
    Task<TenantQueryResult> RunDatasetAsync(Guid datasetId, IReadOnlyDictionary<string, string?>? inputs, int maxRows, Guid actorUserId, CancellationToken ct = default);

    // ---- Superficie para AGENTES: solo opera sobre datasets marcados AgentEnabled (y habilitados) ----

    /// <summary>Datasets del tenant EXPUESTOS a agentes (AgentEnabled + IsEnabled), con su conexion.</summary>
    Task<IReadOnlyList<AgentDatasetInfo>> AgentListDatasetsAsync(CancellationToken ct = default);

    /// <summary>Detalle (nombre, descripcion, parametros con descripcion) de un dataset expuesto a agentes.
    /// Null si no existe o no esta expuesto.</summary>
    Task<AgentDatasetDetail?> AgentGetDatasetAsync(Guid datasetId, CancellationToken ct = default);

    /// <summary>Ejecuta un dataset EXPUESTO a agentes con los valores dados. Rechaza si no esta expuesto.</summary>
    Task<TenantQueryResult> AgentRunDatasetAsync(Guid datasetId, IReadOnlyDictionary<string, string?>? inputs, int maxRows, Guid actorUserId, CancellationToken ct = default);
}
