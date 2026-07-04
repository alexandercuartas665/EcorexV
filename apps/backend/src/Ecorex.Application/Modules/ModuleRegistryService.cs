using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Organization;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Modules;

/// <summary>
/// Implementacion de IModuleRegistryService (ADR-0017). El catalogo ModuleDefinition es
/// GLOBAL (sin filtro de tenant); TenantModule es tenant-scoped y el filtro global aisla
/// el estado de cada tenant. GetEnabledModulesAsync usa IgnoreQueryFilters SOLO tras
/// verificar que el tenant pedido es el activo (o que no hay tenant ambiente: PlatformAdmin).
/// </summary>
public sealed class ModuleRegistryService : IModuleRegistryService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public ModuleRegistryService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    // ---- Catalogo ----

    public async Task<IReadOnlyList<ModuleCatalogRowDto>> ListCatalogAsync(CancellationToken cancellationToken = default)
    {
        var definitions = await _db.ModuleDefinitions.AsNoTracking()
            .OrderBy(d => d.Area).ThenBy(d => d.LegacyCode)
            .ToListAsync(cancellationToken);
        // Estado del tenant activo (el filtro global limita al tenant; sin tenant = sin filas).
        var states = await _db.TenantModules.AsNoTracking()
            .ToDictionaryAsync(tm => tm.ModuleDefinitionId, cancellationToken);
        return definitions.Select(d =>
        {
            states.TryGetValue(d.Id, out var state);
            return ToRow(d, state);
        }).ToList();
    }

    public async Task<OrgResult<ModuleCatalogRowDto>> UpsertDefinitionAsync(
        SaveModuleDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.LegacyCode) || request.LegacyCode.Trim().Length != 6
            || !request.LegacyCode.Trim().All(char.IsAsciiDigit))
        {
            return OrgResult<ModuleCatalogRowDto>.Invalid("El codigo legacy debe tener 6 digitos (ej. 000850).");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return OrgResult<ModuleCatalogRowDto>.Invalid("El nombre es obligatorio.");
        }

        var code = request.LegacyCode.Trim();
        var definition = await _db.ModuleDefinitions
            .FirstOrDefaultAsync(d => d.LegacyCode == code, cancellationToken);
        if (definition is null)
        {
            definition = new ModuleDefinition { LegacyCode = code };
            _db.ModuleDefinitions.Add(definition);
        }
        definition.Name = request.Name.Trim();
        definition.Description = Normalize(request.Description);
        definition.Route = Normalize(request.Route);
        definition.Area = request.Area;
        definition.IsCore = request.IsCore;
        await _db.SaveChangesAsync(cancellationToken);
        return OrgResult<ModuleCatalogRowDto>.Ok(ToRow(definition, null));
    }

    // ---- Estado por tenant ----

    public async Task<OrgResult<ModuleCatalogRowDto>> SetModuleEnabledAsync(
        Guid moduleDefinitionId, bool enabled, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return OrgResult<ModuleCatalogRowDto>.Invalid("No hay tenant activo.");
        }
        var definition = await _db.ModuleDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == moduleDefinitionId, cancellationToken);
        if (definition is null)
        {
            return OrgResult<ModuleCatalogRowDto>.NotFound("El modulo no existe en el catalogo.");
        }
        if (definition.IsCore && !enabled)
        {
            return OrgResult<ModuleCatalogRowDto>.Invalid("Un modulo nucleo no se puede deshabilitar.");
        }

        var state = await _db.TenantModules
            .FirstOrDefaultAsync(tm => tm.ModuleDefinitionId == moduleDefinitionId, cancellationToken);
        if (state is null)
        {
            state = new TenantModule { TenantId = tenantId, ModuleDefinitionId = moduleDefinitionId };
            _db.TenantModules.Add(state);
        }
        state.IsEnabled = enabled;
        await _db.SaveChangesAsync(cancellationToken);
        return OrgResult<ModuleCatalogRowDto>.Ok(ToRow(definition, state));
    }

    public async Task<OrgResult<ModuleCatalogRowDto>> UpdateSettingsAsync(
        Guid moduleDefinitionId, string? settingsJson, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return OrgResult<ModuleCatalogRowDto>.Invalid("No hay tenant activo.");
        }
        var normalized = Normalize(settingsJson);
        if (normalized is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(normalized);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return OrgResult<ModuleCatalogRowDto>.Invalid("Los settings deben ser un objeto JSON (ej. {\"clave\":\"valor\"}).");
                }
            }
            catch (JsonException ex)
            {
                return OrgResult<ModuleCatalogRowDto>.Invalid($"JSON invalido: {ex.Message}");
            }
        }

        var definition = await _db.ModuleDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == moduleDefinitionId, cancellationToken);
        if (definition is null)
        {
            return OrgResult<ModuleCatalogRowDto>.NotFound("El modulo no existe en el catalogo.");
        }

        var state = await _db.TenantModules
            .FirstOrDefaultAsync(tm => tm.ModuleDefinitionId == moduleDefinitionId, cancellationToken);
        if (state is null)
        {
            state = new TenantModule { TenantId = tenantId, ModuleDefinitionId = moduleDefinitionId, IsEnabled = false };
            _db.TenantModules.Add(state);
        }
        state.SettingsJson = normalized;
        await _db.SaveChangesAsync(cancellationToken);
        return OrgResult<ModuleCatalogRowDto>.Ok(ToRow(definition, state));
    }

    public async Task<IReadOnlyList<EnabledModuleDto>> GetEnabledModulesAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Fail-closed: un usuario de tenant solo puede consultar SU tenant. Sin tenant
        // ambiente (PlatformAdmin / procesos de plataforma) se permite el tenant explicito.
        if (_tenantContext.TenantId is Guid ambient && ambient != tenantId)
        {
            return [];
        }
        return await _db.TenantModules.IgnoreQueryFilters().AsNoTracking()
            .Where(tm => tm.TenantId == tenantId && tm.IsEnabled)
            .Join(_db.ModuleDefinitions, tm => tm.ModuleDefinitionId, d => d.Id,
                (tm, d) => new { Definition = d, tm.SettingsJson })
            .OrderBy(x => x.Definition.Area).ThenBy(x => x.Definition.LegacyCode)
            .Select(x => new EnabledModuleDto(
                x.Definition.Id, x.Definition.LegacyCode, x.Definition.Name, x.Definition.Route,
                x.Definition.Area, x.Definition.IsCore, x.SettingsJson))
            .ToListAsync(cancellationToken);
    }

    // ---- Internos ----

    private static ModuleCatalogRowDto ToRow(ModuleDefinition definition, TenantModule? state) => new(
        definition.Id, definition.LegacyCode, definition.Name, definition.Description,
        definition.Route, definition.Area, definition.IsCore,
        state?.IsEnabled ?? false, state?.SettingsJson);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
