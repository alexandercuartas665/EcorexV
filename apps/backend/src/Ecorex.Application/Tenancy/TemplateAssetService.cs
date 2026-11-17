using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed record TemplateAssetDto(Guid Id, string FileName, string Url, string? MimeType, long SizeBytes, DateTimeOffset CreatedAt,
    string? Alt = null, int? Width = null, int? Height = null);

public interface ITemplateAssetService
{
    Task<IReadOnlyList<TemplateAssetDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<TemplateAssetDto?> AddAsync(string fileName, string url, string? mimeType, long sizeBytes, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> UpdateAltAsync(Guid id, string? alt, CancellationToken cancellationToken = default);
}

/// <summary>
/// GALERIA de recursos (imagenes) de las plantillas del tenant (correo, cotizacion). Entidad TENANT-SCOPED.
/// El binario lo sube <see cref="Gestor.ITemplateGalleryService"/> (gestor unificado de archivos); aqui se
/// lista, se describe (alt) y se elimina. Eliminar es SOFT-DELETE (ADR-0176): la imagen sale de la galeria
/// pero se sigue sirviendo para los correos ya enviados que la usan.
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
            .Select(a => new TemplateAssetDto(a.Id, a.FileName, a.Url, a.MimeType, a.SizeBytes, a.CreatedAt, a.Alt, a.Width, a.Height))
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
        // Soft-delete: el binario NO se borra (correos ya enviados la siguen mostrando).
        entity.IsDeleted = true;
        entity.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateAltAsync(Guid id, string? alt, CancellationToken cancellationToken = default)
    {
        var entity = await _db.TemplateAssets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (entity is null) { return false; }
        var a = string.IsNullOrWhiteSpace(alt) ? null : alt.Trim();
        entity.Alt = a is { Length: > 200 } ? a[..200] : a;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
