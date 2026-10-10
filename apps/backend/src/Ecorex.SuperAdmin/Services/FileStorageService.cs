using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Ecorex.Application.Common;
using Ecorex.Application.Storage;
using Ecorex.Domain.Entities;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Services;

/// <summary>
/// Gestor UNIFICADO del almacenamiento de archivos (Azure Blob con fallback a disco). Implementa
/// <see cref="IFileStorageService"/>. Vive en presentacion porque usa el SDK de Azure y WebRootPath.
/// La resolucion del contenedor reusa la config global (StorageConfig): blob solo si esta habilitado+valido.
/// Aislamiento por prefijo {tenant:N}/ dentro del contenedor; la ruta nunca depende del nombre del cliente.
/// Multi-tenant por el filtro global sobre StoredFile (un tenant no lee ni borra archivos de otro).
/// </summary>
public sealed class FileStorageService(
    EcorexDbContext db, ISecretProtector protector, IWebHostEnvironment env, ITenantContext tenant) : IFileStorageService
{
    private async Task<BlobContainerClient?> GetBlobContainerAsync(CancellationToken ct)
    {
        var cfg = await db.StorageConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg is null || !cfg.IsEnabled
            || string.IsNullOrWhiteSpace(cfg.ConnectionStringEncrypted)
            || string.IsNullOrWhiteSpace(cfg.ContainerName))
        {
            return null;
        }
        string conn;
        try { conn = protector.Unprotect(cfg.ConnectionStringEncrypted); }
        catch { return null; }
        return new BlobContainerClient(conn, cfg.ContainerName);
    }

    public async Task<StoredFileDto> SaveAsync(FileStorageSaveRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) { throw new InvalidOperationException("No hay tenant activo."); }

        var module = Clean(request.Module, "archivos");
        var ext = SanitizeExt(Path.GetExtension(request.FileName));
        var id = Guid.CreateVersion7();
        var ref1Seg = SafeSeg(request.Ref1);
        // Ruta estable e independiente del nombre del cliente: {module}/{tenant}/{ref1}/{id}{ext}.
        var blobPath = $"{module}/{tenantId:N}/{ref1Seg}/{id:N}{ext}";

        var container = await GetBlobContainerAsync(ct);
        string provider;
        if (container is not null)
        {
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
            var blob = container.GetBlobClient(blobPath);
            var opts = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = request.ContentType } };
            await blob.UploadAsync(BinaryData.FromBytes(request.Content), opts, ct);
            provider = "AzureBlob";
        }
        else
        {
            var dir = Path.Combine(env.WebRootPath, "uploads", module, tenantId.ToString("N"), ref1Seg);
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(Path.Combine(dir, $"{id:N}{ext}"), request.Content, ct);
            provider = "Local";
        }

        var entity = new StoredFile
        {
            Id = id,
            TenantId = tenantId,
            Module = module,
            Ref1 = request.Ref1,
            Ref2 = request.Ref2,
            FileName = Path.GetFileName(request.FileName),
            ContentType = request.ContentType,
            Extension = ext,
            SizeBytes = request.Content.LongLength,
            BlobPath = blobPath,
            Provider = provider,
        };
        db.StoredFiles.Add(entity);
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<IReadOnlyList<StoredFileDto>> ListAsync(string module, string? ref1 = null, string? ref2 = null, CancellationToken ct = default)
    {
        var q = db.StoredFiles.AsNoTracking().Where(f => f.Module == module);
        if (!string.IsNullOrWhiteSpace(ref1)) { q = q.Where(f => f.Ref1 == ref1); }
        if (!string.IsNullOrWhiteSpace(ref2)) { q = q.Where(f => f.Ref2 == ref2); }
        return (await q.OrderBy(f => f.CreatedAt).ToListAsync(ct)).Select(Map).ToList();
    }

    public async Task<StoredFileDto?> GetAsync(Guid fileId, CancellationToken ct = default)
    {
        var f = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fileId, ct);
        return f is null ? null : Map(f);
    }

    public async Task<FileStorageContent?> ReadAsync(Guid fileId, CancellationToken ct = default)
    {
        var f = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fileId, ct);
        if (f is null) { return null; }
        var ctype = string.IsNullOrWhiteSpace(f.ContentType) ? "application/octet-stream" : f.ContentType!;

        if (f.Provider == "AzureBlob")
        {
            var container = await GetBlobContainerAsync(ct);
            if (container is null) { return null; }
            var blob = container.GetBlobClient(f.BlobPath);
            if (!await blob.ExistsAsync(ct)) { return null; }
            var resp = await blob.DownloadContentAsync(ct);
            return new FileStorageContent(resp.Value.Content.ToArray(), ctype, f.FileName);
        }

        var full = Path.Combine(env.WebRootPath, "uploads", f.BlobPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) { return null; }
        return new FileStorageContent(await File.ReadAllBytesAsync(full, ct), ctype, f.FileName);
    }

    public async Task<bool> DeleteAsync(Guid fileId, CancellationToken ct = default)
    {
        var f = await db.StoredFiles.FirstOrDefaultAsync(x => x.Id == fileId, ct);
        if (f is null) { return false; }
        try
        {
            if (f.Provider == "AzureBlob")
            {
                var container = await GetBlobContainerAsync(ct);
                if (container is not null) { await container.GetBlobClient(f.BlobPath).DeleteIfExistsAsync(cancellationToken: ct); }
            }
            else
            {
                var full = Path.Combine(env.WebRootPath, "uploads", f.BlobPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full)) { File.Delete(full); }
            }
        }
        catch { /* best-effort: igual se borra el registro */ }
        db.StoredFiles.Remove(f);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<StoredFileDto?> FindAsync(string module, string? ref1, string? ref2, string fileName, CancellationToken ct = default)
    {
        var fn = Path.GetFileName(fileName);
        var mod = Clean(module, "archivos");
        var f = await db.StoredFiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Module == mod && x.Ref1 == ref1 && x.Ref2 == ref2 && x.FileName == fn, ct);
        return f is null ? null : Map(f);
    }

    private static StoredFileDto Map(StoredFile f) =>
        new(f.Id, f.Module, f.Ref1, f.Ref2, f.FileName, f.ContentType, f.SizeBytes, f.CreatedAt, f.Provider);

    private static string Clean(string? s, string fallback)
    {
        var cleaned = new string((s ?? "").Trim().Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static string SafeSeg(string? s)
    {
        var cleaned = new string((s ?? "").Trim().Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (string.IsNullOrWhiteSpace(cleaned)) { return "_"; }
        return cleaned.Length > 120 ? cleaned[..120] : cleaned;
    }

    private static string SanitizeExt(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) { return ""; }
        var e = ext.Trim().ToLowerInvariant();
        if (!e.StartsWith('.')) { e = "." + e; }
        var body = e[1..];
        if (body.Length is 0 or > 10 || !body.All(char.IsLetterOrDigit)) { return ".bin"; }
        return e;
    }
}
