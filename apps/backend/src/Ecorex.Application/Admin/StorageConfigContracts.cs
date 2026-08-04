namespace Ecorex.Application.Admin;

/// <summary>Vista de la config de almacenamiento para el Super Admin (sin exponer la cadena).</summary>
public sealed record StorageConfigDto(
    string Provider,
    bool HasConnectionString,
    string? ContainerName,
    bool IsEnabled,
    DateTimeOffset? LastValidatedAt);

public sealed record SaveStorageConfigRequest(
    string Provider,
    string? ConnectionString,
    string? ContainerName,
    bool IsEnabled);

/// <summary>
/// Config GLOBAL de almacenamiento (Azure Blob) editable desde el Super Admin. La cadena de conexion
/// se cifra (ISecretProtector); nunca se devuelve ni se loggea en claro. La implementacion vive en la
/// capa de presentacion porque usa el SDK de Azure.
/// </summary>
public interface IStorageConfigService
{
    Task<StorageConfigDto?> GetAsync(CancellationToken cancellationToken = default);
    Task<StorageConfigDto> SaveAsync(SaveStorageConfigRequest request, CancellationToken cancellationToken = default);

    /// <summary>Prueba la conexion al blob (crea/verifica el contenedor). Usa la cadena del request si
    /// llega; si viene vacia, la ya guardada. Devuelve null si OK, o el error legible si fallo.</summary>
    Task<string?> TestConnectionAsync(SaveStorageConfigRequest request, CancellationToken cancellationToken = default);
}
