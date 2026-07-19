using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>Proveedor/modelo/llave resueltos para una llamada de IA.</summary>
public sealed record AiProviderChoice(AiProvider Provider, string ApiKey, string? BaseUrl, string Model);

/// <summary>
/// Resuelve el proveedor de IA que ELIGIO el operador para el paso (entre los que habilito el Super Admin)
/// y descifra su llave. Es un seam propio para que el orquestador del paso de IA se pueda probar sin BD ni
/// DataProtection.
/// </summary>
public interface IAiProviderResolver
{
    /// <summary>Resuelve el proveedor ELEGIDO (por Id de su config del Super Admin) y descifra su llave.
    /// Falla claro (Choice=null + Error) si no se eligio ninguno, si el elegido ya no esta habilitado, o si
    /// la llave no se puede descifrar. El modelo lo fija el Super Admin en ese proveedor.</summary>
    Task<(AiProviderChoice? Choice, string? Error)> ResolveAsync(Guid? providerConfigId, CancellationToken ct = default);
}

public sealed class AiProviderResolver(IApplicationDbContext db, ISecretProtector protector) : IAiProviderResolver
{
    public async Task<(AiProviderChoice? Choice, string? Error)> ResolveAsync(Guid? providerConfigId, CancellationToken ct = default)
    {
        AiProviderConfig? cfg = null;
        if (providerConfigId is { } id)
        {
            cfg = await db.AiProviderConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        }
        return Decide(providerConfigId, cfg, enc => protector.Unprotect(enc));
    }

    /// <summary>Decision PURA (sin BD): dado el proveedor elegido y su config (o null), produce el choice o
    /// un error legible. Fail-closed: exige que el operador haya elegido una IA que siga habilitada y con
    /// llave. Separado para poder probarlo sin EF ni DataProtection.</summary>
    public static (AiProviderChoice? Choice, string? Error) Decide(
        Guid? providerConfigId, AiProviderConfig? cfg, Func<string, string> unprotect)
    {
        if (providerConfigId is null)
        {
            return (null, "El paso de IA no tiene un proveedor elegido. Elige una IA habilitada (Super Admin -> Servidores de IA).");
        }
        if (cfg is null || !cfg.IsEnabled || cfg.ApiKeyEncrypted is null)
        {
            return (null, "El proveedor de IA elegido para este paso ya no esta habilitado. Elige otro en el paso.");
        }
        string apiKey;
        try { apiKey = unprotect(cfg.ApiKeyEncrypted); }
        catch { return (null, "La llave del proveedor de IA no se pudo descifrar; vuelve a guardarla en el Super Admin."); }

        // El modelo lo fija el Super Admin en el proveedor; si no fijo uno, el por defecto del catalogo.
        var model = string.IsNullOrWhiteSpace(cfg.Model) ? AiProviderCatalog.For(cfg.Provider).DefaultModel : cfg.Model!;
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
