using System.Collections.Concurrent;
using System.IO.Compression;
using Ecorex.Application.Integraciones.Maqueta;
using Ecorex.Application.Storage;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Options;

namespace Ecorex.SuperAdmin.Maqueta;

/// <summary>
/// Cache en disco de las versiones de maqueta (ADR-0179). Cada version (inmutable) se descomprime UNA vez desde el
/// gestor de archivos a {CacheDir}/{versionId}/ y se sirve con un StaticFileMiddleware propio (ETag, rangos, HEAD).
/// La cache es desechable: si el contenedor se reinicia, se vuelve a descomprimir en la primera visita.
/// </summary>
public sealed class MockupSiteCache
{
    private readonly string _cacheDir;
    private readonly IWebHostEnvironment _env;
    private readonly ILoggerFactory _logs;
    private readonly ConcurrentDictionary<Guid, Lazy<Task<string>>> _extractions = new();
    private readonly ConcurrentDictionary<Guid, StaticFileMiddleware> _servers = new();

    public MockupSiteCache(IConfiguration configuration, IWebHostEnvironment env, ILoggerFactory logs)
    {
        var configured = configuration["MaquetaNube:CacheDir"];
        _cacheDir = string.IsNullOrWhiteSpace(configured) ? Path.Combine(Path.GetTempPath(), "ecorex-maquetas") : configured;
        _env = env;
        _logs = logs;
    }

    /// <summary>Carpeta descomprimida de la version (la descomprime si hace falta; una sola vez en paralelo).</summary>
    public Task<string> EnsureExtractedAsync(MockupServeTarget target, IFileStorageService files)
    {
        var lazy = _extractions.GetOrAdd(target.VersionId, _ => new Lazy<Task<string>>(() => ExtractAsync(target, files)));
        var task = lazy.Value;
        if (task.IsFaulted || task.IsCanceled)
        {
            // Un fallo (blob no disponible) no queda pegado: el siguiente intento vuelve a probar.
            _extractions.TryRemove(new KeyValuePair<Guid, Lazy<Task<string>>>(target.VersionId, lazy));
        }
        return task;
    }

    /// <summary>
    /// Sirve <paramref name="subPath"/> (relativo a la raiz de la version) si existe. Devuelve false si no hay archivo
    /// servible (oculto, fuera de la carpeta, tipo desconocido o directorio): el llamador sigue el pipeline.
    /// </summary>
    public async Task<bool> TryServeAsync(HttpContext ctx, Guid versionId, string root, PathString subPath)
    {
        var server = _servers.GetOrAdd(versionId, _ => new StaticFileMiddleware(
            next: c => { c.Items[MissKey] = true; return Task.CompletedTask; },
            hostingEnv: _env,
            options: Options.Create(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(root, ExclusionFilters.Sensitive),
                ContentTypeProvider = new FileExtensionContentTypeProvider(),
                ServeUnknownFileTypes = false,
                OnPrepareResponse = c =>
                {
                    c.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                    c.Context.Response.Headers["Cache-Control"] = "no-cache";
                }
            }),
            loggerFactory: _logs));

        var originalPath = ctx.Request.Path;
        var originalBase = ctx.Request.PathBase;
        ctx.Items.Remove(MissKey);
        try
        {
            ctx.Request.PathBase = originalBase.Add(originalPath.Value![..^subPath.Value!.Length]);
            ctx.Request.Path = subPath;
            await server.Invoke(ctx);
        }
        finally
        {
            ctx.Request.Path = originalPath;
            ctx.Request.PathBase = originalBase;
        }
        return !ctx.Items.ContainsKey(MissKey);
    }

    /// <summary>Pantallas de la version (inmutable: se escanea una sola vez) para el editor (ADR-0180).</summary>
    public MockupSiteInfo GetSiteInfo(Guid versionId, string root) => _sites.GetOrAdd(versionId, _ => MockupPages.Scan(root));

    private readonly ConcurrentDictionary<Guid, MockupSiteInfo> _sites = new();

    private const string MissKey = "ecorex.mockup.miss";

    private async Task<string> ExtractAsync(MockupServeTarget target, IFileStorageService files)
    {
        var dest = Path.Combine(_cacheDir, target.VersionId.ToString("N"));
        if (File.Exists(Path.Combine(dest, ".ok"))) { return dest; }

        var content = await files.ReadForTenantAsync(target.ZipFileId, target.TenantId)
            ?? throw new InvalidOperationException($"No se encontro el ZIP de la version {target.VersionId}.");
        var tmp = dest + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(tmp);
        var tmpFull = Path.GetFullPath(tmp) + Path.DirectorySeparatorChar;
        var prefix = target.RootPrefix ?? string.Empty;
        using (var archive = new ZipArchive(new MemoryStream(content.Content, writable: false), ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                var path = MockupService.NormalizeEntry(entry.FullName);
                if (path is null || path.Length == 0 || path.EndsWith('/') || MockupService.IsHiddenPath(path)) { continue; }
                if (prefix.Length > 0)
                {
                    if (!path.StartsWith(prefix, StringComparison.Ordinal)) { continue; }
                    path = path[prefix.Length..];
                }
                var full = Path.GetFullPath(Path.Combine(tmp, path.Replace('/', Path.DirectorySeparatorChar)));
                if (!full.StartsWith(tmpFull, StringComparison.OrdinalIgnoreCase)) { continue; } // zip-slip
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await using var input = entry.Open();
                await using var output = File.Create(full);
                await input.CopyToAsync(output);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(tmp, ".ok"), DateTimeOffset.UtcNow.ToString("O"));

        try
        {
            if (Directory.Exists(dest)) { Directory.Delete(dest, recursive: true); }
            Directory.Move(tmp, dest);
        }
        catch (IOException) when (File.Exists(Path.Combine(dest, ".ok")))
        {
            // Otra instancia gano la carrera: se usa la suya.
            try { Directory.Delete(tmp, recursive: true); } catch (IOException) { }
        }
        _logs.CreateLogger("MaquetaNube").LogInformation("Maqueta: version {VersionId} descomprimida en cache.", target.VersionId);
        return dest;
    }
}
