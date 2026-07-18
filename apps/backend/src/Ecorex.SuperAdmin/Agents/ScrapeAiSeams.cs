using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>Proveedor/modelo/llave resueltos para una llamada de IA.</summary>
public sealed record AiProviderChoice(AiProvider Provider, string ApiKey, string? BaseUrl, string Model);

/// <summary>
/// Resuelve QUE proveedor de IA usar (entre los que habilito el Super Admin) y descifra su llave. Es un
/// seam propio para que el orquestador del paso de IA se pueda probar sin BD ni DataProtection.
/// </summary>
public interface IAiProviderResolver
{
    /// <summary>Elige un proveedor habilitado y descifra su llave. Devuelve el error legible si no hay
    /// ninguno o la llave no se puede descifrar.</summary>
    Task<(AiProviderChoice? Choice, string? Error)> ResolveAsync(string? preferredModel, CancellationToken ct = default);
}

public sealed class AiProviderResolver(IApplicationDbContext db, ISecretProtector protector) : IAiProviderResolver
{
    public async Task<(AiProviderChoice? Choice, string? Error)> ResolveAsync(string? preferredModel, CancellationToken ct = default)
    {
        var cfg = await db.AiProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsEnabled && c.ApiKeyEncrypted != null, ct);
        if (cfg is null)
        {
            return (null, "No hay un proveedor de IA habilitado. Configuralo en el Super Admin (AI Gateway).");
        }
        string apiKey;
        try { apiKey = protector.Unprotect(cfg.ApiKeyEncrypted!); }
        catch { return (null, "La llave del proveedor de IA no se pudo descifrar; vuelve a guardarla en el Super Admin."); }

        var model = !string.IsNullOrWhiteSpace(preferredModel) ? preferredModel!
            : cfg.Model ?? AiProviderCatalog.For(cfg.Provider).DefaultModel;
        return (new AiProviderChoice(cfg.Provider, apiKey, cfg.BaseUrl, model), null);
    }
}

/// <summary>Sumidero de filas extraidas por un paso de IA. Seam sobre <see cref="ScrapeRowIngest"/> para
/// aislar el orquestador de la BD/ingesta en las pruebas.</summary>
public interface IScrapeRowSink
{
    Task<(int Inserted, int Updated, int Deleted)> IngestAsync(
        Guid containerId, Guid tenantId, string? mappingJson,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows, CancellationToken ct = default);
}

public sealed class ScrapeRowSink(IRowIngestService ingest, IApplicationDbContext db) : IScrapeRowSink
{
    public Task<(int Inserted, int Updated, int Deleted)> IngestAsync(
        Guid containerId, Guid tenantId, string? mappingJson,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows, CancellationToken ct = default)
        => ScrapeRowIngest.IngestAsync(ingest, db, containerId, tenantId, mappingJson, rows, ct);
}
