using Ecorex.Application.Common;
using Ecorex.Application.Storage;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;

namespace Ecorex.Application.Gestor;

public interface ITemplateGalleryService
{
    /// <summary>Sube una imagen a la galeria del tenant. Valida el formato por su CONTENIDO (PNG/JPG/GIF), lee
    /// sus dimensiones y la deja servible en una URL publica (/i/...).</summary>
    Task<(TemplateAssetDto? Asset, string? Error)> UploadAsync(string fileName, byte[] content, string? alt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Subida de imagenes a la GALERIA DE PLANTILLAS (ADR-0177). El binario va al gestor unificado de archivos
/// (Azure Blob o disco, modulo <see cref="Module"/>) y la fila TemplateAsset guarda la URL publica /i/{archivo}
/// que sirve el endpoint anonimo (los clientes de correo no tienen sesion). Formatos aptos para correo: PNG,
/// JPG y GIF (SVG y WEBP no se ven en Gmail/Outlook). Vive en Application pero se registra en la consola
/// (depende de IFileStorageService, que solo existe ahi).
/// </summary>
public sealed class TemplateGalleryService : ITemplateGalleryService
{
    /// <summary>Modulo del gestor de archivos para las imagenes de la galeria (lo usa el endpoint publico).</summary>
    public const string Module = "galeria-plantillas";

    /// <summary>Tope por imagen. Correos pesados cargan lento y algunos clientes no muestran imagenes enormes.</summary>
    public const int MaxBytes = 3 * 1024 * 1024;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IFileStorageService _files;

    public TemplateGalleryService(IApplicationDbContext db, ITenantContext tenant, IFileStorageService files)
    {
        _db = db;
        _tenant = tenant;
        _files = files;
    }

    public async Task<(TemplateAssetDto? Asset, string? Error)> UploadAsync(string fileName, byte[] content, string? alt, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return (null, "Sin tenant activo."); }
        if (content is null || content.Length == 0) { return (null, "El archivo esta vacio."); }
        if (content.Length > MaxBytes) { return (null, $"La imagen pesa {content.Length / 1024} KB; el maximo es {MaxBytes / 1024 / 1024} MB (ideal < 300 KB para correos)."); }

        var kind = Sniff(content);
        if (kind is null) { return (null, "Formato no admitido: usa PNG, JPG o GIF (SVG y WEBP no se ven en Gmail/Outlook)."); }
        var (mime, ext) = kind.Value;
        var dims = ImageDimensions.TryRead(content);

        var display = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "imagen" + ext : fileName.Trim());
        if (display.Length > 200) { display = display[..200]; }
        var assetId = Guid.CreateVersion7();
        // Ref1 = id del recurso: la llave logica del gestor (modulo+ref1+ref2+nombre) es unica, asi se puede subir
        // dos veces un archivo con el mismo nombre.
        var stored = await _files.SaveAsync(new FileStorageSaveRequest(Module, assetId.ToString("N"), null, display, mime, content), cancellationToken);

        var a = string.IsNullOrWhiteSpace(alt) ? Path.GetFileNameWithoutExtension(display).Replace('_', ' ').Replace('-', ' ').Trim() : alt.Trim();
        var entity = new TemplateAsset
        {
            Id = assetId,
            TenantId = tenantId,
            FileName = display,
            Url = PublicPath(stored.Id, ext),
            MimeType = mime,
            SizeBytes = content.LongLength,
            Alt = a.Length > 200 ? a[..200] : a,
            Width = dims?.Width,
            Height = dims?.Height,
            StoredFileId = stored.Id,
        };
        _db.TemplateAssets.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return (new TemplateAssetDto(entity.Id, entity.FileName, entity.Url, entity.MimeType, entity.SizeBytes, entity.CreatedAt, entity.Alt, entity.Width, entity.Height), null);
    }

    /// <summary>Ruta publica de una imagen de la galeria: /i/{id del archivo}{ext}.</summary>
    public static string PublicPath(Guid storedFileId, string ext) => $"/i/{storedFileId:N}{ext}";

    /// <summary>Formato real por la firma del contenido (no se confia en el content-type del navegador).</summary>
    internal static (string Mime, string Ext)? Sniff(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == (byte)'P' && d[2] == (byte)'N' && d[3] == (byte)'G') { return ("image/png", ".png"); }
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) { return ("image/jpeg", ".jpg"); }
        if (d.Length >= 6 && d[0] == (byte)'G' && d[1] == (byte)'I' && d[2] == (byte)'F' && d[3] == (byte)'8') { return ("image/gif", ".gif"); }
        return null;
    }
}
