using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion GLOBAL de un modulo de la plataforma (module registry, legacy 000109).
/// Es el catalogo unico compartido por todos los tenants: NO es tenant-scoped y NO recibe
/// query filter (ADR-0017). Solo el PlatformAdmin lo edita; los tenants lo leen y activan
/// su estado por tenant en <see cref="TenantModule"/>.
/// </summary>
public class ModuleDefinition : BaseEntity
{
    /// <summary>Codigo del modulo legacy (6 digitos, ej. "000850"). Unico global.</summary>
    public string LegacyCode { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>Ruta de la consola que sirve el modulo (ej. "/dependencias"). Null si aun no tiene UI.</summary>
    public string? Route { get; set; }

    public ModuleArea Area { get; set; } = ModuleArea.Principal;

    /// <summary>Modulo nucleo del producto: no se puede deshabilitar por tenant.</summary>
    public bool IsCore { get; set; }
}
