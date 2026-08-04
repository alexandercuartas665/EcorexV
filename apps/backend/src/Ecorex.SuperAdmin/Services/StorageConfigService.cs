using Azure.Storage.Blobs;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Services;

/// <summary>
/// Config GLOBAL de almacenamiento (Azure Blob), editable desde el Super Admin. Vive en presentacion
/// porque usa el SDK de Azure. La cadena de conexion se cifra con ISecretProtector; nunca se devuelve
/// ni se loggea en claro. El contenedor se crea PRIVADO (acceso solo por el servidor, ADR proxy).
/// </summary>
public sealed class StorageConfigService : IStorageConfigService
{
    private readonly EcorexDbContext _db;
    private readonly ISecretProtector _protector;

    public StorageConfigService(EcorexDbContext db, ISecretProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async Task<StorageConfigDto?> GetAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await _db.StorageConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return cfg is null ? null : Map(cfg);
    }

    public async Task<StorageConfigDto> SaveAsync(SaveStorageConfigRequest request, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.StorageConfigs.FirstOrDefaultAsync(cancellationToken);
        if (cfg is null)
        {
            cfg = new StorageConfig();
            _db.StorageConfigs.Add(cfg);
        }

        cfg.Provider = string.IsNullOrWhiteSpace(request.Provider) ? "AzureBlob" : request.Provider.Trim();
        cfg.ContainerName = request.ContainerName?.Trim();
        cfg.IsEnabled = request.IsEnabled;

        // La cadena solo se re-cifra si llega un valor nuevo; vacia conserva la actual.
        if (!string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            cfg.ConnectionStringEncrypted = _protector.Protect(request.ConnectionString.Trim());
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Map(cfg);
    }

    public async Task<string?> TestConnectionAsync(SaveStorageConfigRequest request, CancellationToken cancellationToken = default)
    {
        // Cadena a probar: la del request si llega; si no, la ya guardada (descifrada).
        string? conn = request.ConnectionString?.Trim();
        if (string.IsNullOrWhiteSpace(conn))
        {
            var cfg = await _db.StorageConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            if (cfg?.ConnectionStringEncrypted is { } enc)
            {
                try { conn = _protector.Unprotect(enc); }
                catch { return "La cadena guardada esta cifrada con una version anterior. Vuelve a pegarla."; }
            }
        }
        if (string.IsNullOrWhiteSpace(conn))
        {
            return "Falta la cadena de conexion.";
        }
        var container = string.IsNullOrWhiteSpace(request.ContainerName) ? "ecorex-uploads" : request.ContainerName.Trim();

        try
        {
            var client = new BlobContainerClient(conn, container);
            // Crea el contenedor si no existe (privado por defecto): valida credenciales + permisos.
            await client.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            // Confirma acceso real listando (una pagina minima).
            await foreach (var _ in client.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                break;
            }
            var cfg = await _db.StorageConfigs.FirstOrDefaultAsync(cancellationToken);
            if (cfg is not null)
            {
                cfg.LastValidatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }
            return null;
        }
        catch (Exception ex)
        {
            return $"No se pudo conectar al blob: {ex.Message}";
        }
    }

    private static StorageConfigDto Map(StorageConfig c) => new(
        c.Provider,
        !string.IsNullOrEmpty(c.ConnectionStringEncrypted),
        c.ContainerName,
        c.IsEnabled,
        c.LastValidatedAt);
}
