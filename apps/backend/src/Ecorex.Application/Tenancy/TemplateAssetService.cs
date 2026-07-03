using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed record TemplateAssetDto(Guid Id, string FileName, string Url, string? MimeType, long SizeBytes, DateTimeOffset CreatedAt);

public interface ITemplateAssetService
{
    Task<IReadOnlyList<TemplateAssetDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<TemplateAssetDto?> AddAsync(string fileName, string url, string? mimeType, long sizeBytes, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Galeria de recursos (imagenes) de las plantillas de cotizacion por agencia. Entidad TENANT-SCOPED.
/// Los archivos se guardan en disco por la UI; aqui solo se registra la referencia (URL) reutilizable
/// por cualquier plantilla.
/// </summary>
public sealed class TemplateAssetService : ITemplateAssetService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public TemplateAssetService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<TemplateAssetDto>> ListAsync(CancellationToken cancellationToken = default)
        => await _db.TemplateAssets
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new TemplateAssetDto(a.Id, a.FileName, a.Url, a.MimeType, a.SizeBytes, a.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<TemplateAssetDto?> AddAsync(string fileName, string url, string? mimeType, long sizeBytes, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var entity = new TemplateAsset
        {
            TenantId = tenantId,
            FileName = (fileName ?? "imagen").Trim(),
            Url = url,
            MimeType = mimeType,
            SizeBytes = sizeBytes
        };
        _db.TemplateAssets.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return new TemplateAssetDto(entity.Id, entity.FileName, entity.Url, entity.MimeType, entity.SizeBytes, entity.CreatedAt);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.TemplateAssets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (entity is null) { return false; }
        _db.TemplateAssets.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
