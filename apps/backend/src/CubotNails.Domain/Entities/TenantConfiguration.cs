using CubotNails.Domain.Common;

namespace CubotNails.Domain.Entities;

/// <summary>
/// Configuracion comercial base del tenant (clave/valor). Entidad TENANT-SCOPED.
/// </summary>
public class TenantConfiguration : TenantEntity
{
    public string ConfigKey { get; set; } = null!;
    public string? ConfigValue { get; set; }
}
