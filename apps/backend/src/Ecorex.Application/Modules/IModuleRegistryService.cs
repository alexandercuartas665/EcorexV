using Ecorex.Application.Organization;

namespace Ecorex.Application.Modules;

/// <summary>
/// Registro de modulos (module registry, legacy 000109, ADR-0017). El catalogo
/// (ModuleDefinition) es GLOBAL de plataforma: los tenants SOLO lo leen; unicamente el
/// PlatformAdmin lo edita (la UI/endpoint que use Upsert debe exigir la policy
/// SuperAdminOnly / PlatformOperator existente). El estado por tenant (TenantModule:
/// habilitado + settings JSON) es tenant-scoped y lo administra el owner/admin del tenant.
///
/// TODO(policies por modulo): cuando existan policies propias (ej. "Modulo.000850.Usar"),
/// GetEnabledModulesAsync sera la fuente del menu de la consola: cada item del NavMenu se
/// derivara de este registry (ruta + area + codigo legacy) filtrado por la policy del
/// modulo, en vez del menu estatico actual.
/// </summary>
public interface IModuleRegistryService
{
    // ---- Catalogo (lectura para tenants) ----

    /// <summary>Catalogo global completo con el estado del tenant activo (deshabilitado si no hay fila).</summary>
    Task<IReadOnlyList<ModuleCatalogRowDto>> ListCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea o actualiza una definicion del catalogo GLOBAL por LegacyCode. SOLO PlatformAdmin
    /// (el llamador debe exigir la policy de plataforma; el seeder tambien la usa).
    /// </summary>
    Task<OrgResult<ModuleCatalogRowDto>> UpsertDefinitionAsync(SaveModuleDefinitionRequest request, CancellationToken cancellationToken = default);

    // ---- Estado por tenant ----

    /// <summary>Habilita/deshabilita el modulo para el tenant activo (owner/admin o PlatformAdmin). Invalid si IsCore y se intenta deshabilitar.</summary>
    Task<OrgResult<ModuleCatalogRowDto>> SetModuleEnabledAsync(Guid moduleDefinitionId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Guarda los settings JSON del modulo para el tenant activo. Invalid si el texto no es un objeto JSON.</summary>
    Task<OrgResult<ModuleCatalogRowDto>> UpdateSettingsAsync(Guid moduleDefinitionId, string? settingsJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Modulos habilitados de un tenant, pensado para derivar el menu futuro del registry.
    /// Fail-closed: si hay tenant activo y no coincide con <paramref name="tenantId"/>,
    /// devuelve vacio (solo el PlatformAdmin, sin tenant ambiente, consulta cualquier tenant).
    /// </summary>
    Task<IReadOnlyList<EnabledModuleDto>> GetEnabledModulesAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
