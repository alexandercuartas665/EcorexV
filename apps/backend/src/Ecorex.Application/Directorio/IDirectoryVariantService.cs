using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Directorio;

/// <summary>Variante de UI del Directorio General que usa el tenant. La elige el cliente en la
/// configuracion de su empresa; el Directorio renderiza la pagina correspondiente.</summary>
public enum DirectoryVariant
{
    /// <summary>La vista actual (por defecto).</summary>
    Ligero,

    /// <summary>Vista alterna (copia independiente), para tenants que prefieren otro layout.</summary>
    Especializado
}

/// <summary>
/// Lee/escribe la variante del Directorio elegida por el tenant activo. Se guarda en
/// <see cref="TenantConfiguration"/> (clave/valor, tenant-scoped) bajo <see cref="ConfigKey"/>. Sin fila
/// o valor desconocido => <see cref="DirectoryVariant.Ligero"/> (por defecto, no rompe a nadie).
/// </summary>
public interface IDirectoryVariantService
{
    Task<DirectoryVariant> GetAsync(CancellationToken ct = default);

    Task SetAsync(DirectoryVariant variant, CancellationToken ct = default);
}

public sealed class DirectoryVariantService : IDirectoryVariantService
{
    /// <summary>Clave en TenantConfiguration.</summary>
    public const string ConfigKey = "directorio.variante";

    private const string ValueEspecializado = "especializado";
    private const string ValueLigero = "ligero";

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public DirectoryVariantService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<DirectoryVariant> GetAsync(CancellationToken ct = default)
    {
        // Tenant-safe: TenantConfiguration lleva el filtro global; solo ve la fila de su tenant.
        var value = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == ConfigKey)
            .Select(c => c.ConfigValue)
            .FirstOrDefaultAsync(ct);

        return string.Equals(value, ValueEspecializado, StringComparison.OrdinalIgnoreCase)
            ? DirectoryVariant.Especializado
            : DirectoryVariant.Ligero;
    }

    public async Task SetAsync(DirectoryVariant variant, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("No hay tenant activo para guardar la variante del Directorio.");
        }

        var value = variant == DirectoryVariant.Especializado ? ValueEspecializado : ValueLigero;
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == ConfigKey, ct);
        if (row is null)
        {
            _db.TenantConfigurations.Add(new TenantConfiguration
            {
                TenantId = tenantId,
                ConfigKey = ConfigKey,
                ConfigValue = value
            });
        }
        else
        {
            row.ConfigValue = value;
        }

        await _db.SaveChangesAsync(ct);
    }
}
