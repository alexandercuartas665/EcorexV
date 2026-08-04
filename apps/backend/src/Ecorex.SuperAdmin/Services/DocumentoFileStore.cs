using Azure.Storage.Blobs;
using Ecorex.Application.Common;
using Ecorex.Application.Documentos;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Services;

/// <summary>
/// Almacen de los binarios del Gestor Documental sobre el sistema de archivos, en
/// wwwroot/uploads/documentos/{tenant}. Mismo patron que items, avatares y adjuntos del chat.
///
/// Vive en la capa de presentacion porque es la unica que conoce WebRootPath. El servicio de
/// aplicacion solo ve <see cref="IDocumentoFileStore"/>, asi que mover esto a S3/R2 mas adelante
/// no toca ni una linea de la logica documental.
///
/// AISLAMIENTO: la carpeta por tenant + nombre generado con Guid hacen imposible que un tenant
/// pise el archivo de otro, y que el nombre que mando el cliente influya en la ruta.
///
/// DEUDA CONOCIDA (ya anotada en PROGRESO.md): el backup.sh de produccion respalda la BD pero NO
/// wwwroot/uploads. Mientras siga asi, un restore deja los metadatos sin sus archivos.
/// </summary>
public sealed class DocumentoFileStore : IDocumentoFileStore
{
    private const string CarpetaRaiz = "documentos";

    private readonly IWebHostEnvironment _env;
    private readonly EcorexDbContext _db;
    private readonly ISecretProtector _protector;

    public DocumentoFileStore(IWebHostEnvironment env, EcorexDbContext db, ISecretProtector protector)
    {
        _env = env;
        _db = db;
        _protector = protector;
    }

    /// <summary>
    /// Contenedor de Azure Blob si el almacenamiento global esta habilitado y valido; null = usar disco.
    /// La URL publica sigue el MISMO esquema /uploads/documentos/... para no romper los enlaces ya
    /// guardados; el blob se guarda en la ruta documentos/{tenant}/{archivo} dentro del contenedor.
    /// </summary>
    private async Task<BlobContainerClient?> GetBlobContainerAsync(CancellationToken ct)
    {
        var cfg = await _db.StorageConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (cfg is null || !cfg.IsEnabled
            || string.IsNullOrWhiteSpace(cfg.ConnectionStringEncrypted)
            || string.IsNullOrWhiteSpace(cfg.ContainerName))
        {
            return null;
        }
        string conn;
        try { conn = _protector.Unprotect(cfg.ConnectionStringEncrypted); }
        catch { return null; }
        return new BlobContainerClient(conn, cfg.ContainerName);
    }

    /// <summary>Ruta del blob a partir de la URL publica (/uploads/documentos/{t}/{f} -> documentos/{t}/{f}).</summary>
    private static string BlobPath(string urlPublica)
        => urlPublica.TrimStart('/').StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)
            ? urlPublica.TrimStart('/')["uploads/".Length..]
            : urlPublica.TrimStart('/');

    public async Task<string> SaveAsync(
        Guid tenantId, byte[] contenido, string extension, CancellationToken ct = default)
    {
        // La extension llega YA validada contra la lista blanca de DocumentUploadGuard. Se vuelve a
        // sanear aqui por si otro consumidor llamara a este almacen sin pasar por el guard.
        var ext = Sanear(extension);
        var nombre = DocumentUploadGuard.BuildStoredFileName("doc", ext);
        var urlPublica = $"/uploads/{CarpetaRaiz}/{tenantId:N}/{nombre}";

        var container = await GetBlobContainerAsync(ct);
        if (container is not null)
        {
            // Aislamiento por prefijo de carpeta {tenant} dentro del contenedor (igual que en disco).
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
            var blob = container.GetBlobClient($"{CarpetaRaiz}/{tenantId:N}/{nombre}");
            await blob.UploadAsync(BinaryData.FromBytes(contenido), overwrite: true, ct);
            return urlPublica;
        }

        var dir = Path.Combine(_env.WebRootPath, "uploads", CarpetaRaiz, tenantId.ToString("N"));
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, nombre), contenido, ct);
        return urlPublica;
    }

    public async Task<byte[]?> ReadAsync(string urlPublica, CancellationToken ct = default)
    {
        // Blob primero (si esta habilitado y el archivo existe alli); si no, disco. Asi los archivos
        // viejos (en disco) siguen leyendose aunque el blob ya este activo.
        var container = await GetBlobContainerAsync(ct);
        if (container is not null)
        {
            var blob = container.GetBlobClient(BlobPath(urlPublica));
            if (await blob.ExistsAsync(ct))
            {
                var resp = await blob.DownloadContentAsync(ct);
                return resp.Value.Content.ToArray();
            }
        }

        var ruta = ResolverRuta(urlPublica);
        if (ruta is null || !File.Exists(ruta)) { return null; }
        return await File.ReadAllBytesAsync(ruta, ct);
    }

    /// <summary>
    /// Traduce la URL publica a una ruta fisica, comprobando que el resultado siga DENTRO de
    /// wwwroot/uploads/documentos. La URL sale de la BD, pero si algun dia entrara por otra via un
    /// "../../appsettings.json" no debe poder leerse: la comprobacion se hace sobre la ruta ya
    /// resuelta, que es lo unico que no se puede enganar con secuencias relativas.
    /// </summary>
    private string? ResolverRuta(string urlPublica)
    {
        if (string.IsNullOrWhiteSpace(urlPublica)) { return null; }

        var relativa = urlPublica.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var raiz = Path.GetFullPath(Path.Combine(_env.WebRootPath, "uploads", CarpetaRaiz));
        string completa;
        try
        {
            completa = Path.GetFullPath(Path.Combine(_env.WebRootPath, relativa));
        }
        catch (ArgumentException)
        {
            return null;
        }

        var prefijo = raiz.EndsWith(Path.DirectorySeparatorChar) ? raiz : raiz + Path.DirectorySeparatorChar;
        return completa.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase) ? completa : null;
    }

    /// <summary>Extension segura: solo letras y digitos tras el punto; si no, ".bin".</summary>
    private static string Sanear(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) { return ".bin"; }
        var ext = extension.Trim().ToLowerInvariant();
        if (!ext.StartsWith('.')) { ext = "." + ext; }
        var cuerpo = ext[1..];
        if (cuerpo.Length is 0 or > 10 || !cuerpo.All(char.IsLetterOrDigit)) { return ".bin"; }
        return ext;
    }
}
