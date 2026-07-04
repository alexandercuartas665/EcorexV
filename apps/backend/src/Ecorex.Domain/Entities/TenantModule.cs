using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Estado de un modulo del catalogo global para UN tenant (module registry, legacy 000109):
/// habilitado/deshabilitado y settings propios del tenant como documento JSON (jsonb en
/// PostgreSQL, nvarchar(max) en SQL Server). Unico por (TenantId, ModuleDefinitionId).
/// TENANT-SCOPED (a diferencia del catalogo <see cref="ModuleDefinition"/>, que es global).
/// </summary>
public class TenantModule : TenantEntity
{
    public Guid ModuleDefinitionId { get; set; }
    public ModuleDefinition? ModuleDefinition { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Configuracion del modulo especifica del tenant (objeto JSON o null).</summary>
    public string? SettingsJson { get; set; }
}
