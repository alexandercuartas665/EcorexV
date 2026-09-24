using Ecorex.Application.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Ecorex.SuperAdmin.Services;

/// <inheritdoc />
/// <remarks>
/// Guarda el archivo bajo <c>wwwroot/uploads/templates/{guid}/{archivo}</c> (mismo convenio que la subida
/// del encabezado en Plantillas WhatsApp) y lo sirve el static-files de <c>/uploads</c>. La URL absoluta se
/// arma con la base publica del entorno para que Meta/YCloud pueda descargarla.
/// </remarks>
public sealed class TemplateMediaStore : ITemplateMediaStore
{
    private readonly IWebHostEnvironment _env;
    private readonly string? _publicBase;

    public TemplateMediaStore(IWebHostEnvironment env, IConfiguration configuration)
    {
        _env = env;
        var url = configuration["Ecorex:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(url))
        {
            url = Environment.GetEnvironmentVariable("ECOREX_PUBLIC_BASE_URL");
        }
        _publicBase = string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');
    }

    public async Task<string?> PublishAsync(byte[] bytes, string fileName, CancellationToken cancellationToken = default)
    {
        // Sin base publica no se puede construir una URL que Meta alcance -> null (el llamador lo registra).
        if (_publicBase is null || bytes is null || bytes.Length == 0) { return null; }

        var leaf = SafeLeaf(fileName);
        var sub = Guid.NewGuid().ToString("N");   // aisla la escritura (sin path traversal) y hace la URL no adivinable
        var root = string.IsNullOrWhiteSpace(_env.WebRootPath) ? "wwwroot" : _env.WebRootPath;
        var dir = Path.Combine(root, "uploads", "templates", sub);
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, leaf), bytes, cancellationToken);
        return $"{_publicBase}/uploads/templates/{sub}/{Uri.EscapeDataString(leaf)}";
    }

    // Nombre de archivo seguro (solo la hoja, sin rutas), con extension .pdf por defecto.
    private static string SafeLeaf(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) { name = "documento.pdf"; }
        var safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ' ? c : '-').ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safe)) { safe = "documento.pdf"; }
        if (!safe.Contains('.')) { safe += ".pdf"; }
        return safe;
    }
}
