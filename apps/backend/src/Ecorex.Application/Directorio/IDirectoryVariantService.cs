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
    Especializado,

    /// <summary>2do motor de contactos: "Directorio Modular" (secciones + categorias componibles,
    /// Capa 8). La logica de seleccion/mapeo de este valor se completa en la capa Application.</summary>
    Modular
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

    /// <summary>Si el tenant activo puede usar el motor Modular (2do motor de contactos). Hoy gateado a
    /// SOLDARCO (unico cliente que lo usa); asi el selector no ofrece la opcion a otros tenants.</summary>
    Task<bool> CanUseModularAsync(CancellationToken ct = default);
}

public sealed class DirectoryVariantService : IDirectoryVariantService
{
    /// <summary>Clave en TenantConfiguration.</summary>
    public const string ConfigKey = "directorio.variante";

    /// <summary>Tenant SOLDARCO: unico habilitado para el motor Modular (Capa 8). Gate explicito para no
    /// exponer el 2do motor a otros tenants hasta que se generalice.</summary>
    public static readonly Guid SoldarcoTenantId = Guid.Parse("e3519cc4-150f-4f63-a0cd-21eb9d59f1fa");

    private const string ValueEspecializado = "especializado";
    private const string ValueModular = "modular";
    private const string ValueLigero = "ligero";

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public DirectoryVariantService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public Task<bool> CanUseModularAsync(CancellationToken ct = default)
        => Task.FromResult(_tenant.TenantId == SoldarcoTenantId);

    public async Task<DirectoryVariant> GetAsync(CancellationToken ct = default)
    {
        // Tenant-safe: TenantConfiguration lleva el filtro global; solo ve la fila de su tenant.
        var value = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == ConfigKey)
            .Select(c => c.ConfigValue)
            .FirstOrDefaultAsync(ct);

        // El motor Modular solo aplica si el tenant esta habilitado; si no, cae a Ligero (no rompe a nadie).
        if (string.Equals(value, ValueModular, StringComparison.OrdinalIgnoreCase))
        {
            return _tenant.TenantId == SoldarcoTenantId ? DirectoryVariant.Modular : DirectoryVariant.Ligero;
        }
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
        if (variant == DirectoryVariant.Modular && tenantId != SoldarcoTenantId)
        {
            throw new InvalidOperationException("El motor Modular no esta habilitado para este tenant.");
        }

        var value = variant switch
        {
            DirectoryVariant.Especializado => ValueEspecializado,
            DirectoryVariant.Modular => ValueModular,
            _ => ValueLigero
        };
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
