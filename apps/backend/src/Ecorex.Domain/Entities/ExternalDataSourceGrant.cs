using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Concesion EXPLICITA de una <see cref="ExternalDataSource"/> a un tenant (ADR-0064). El dato externo
/// NO se filtra solo por tenant (vive en otra base), asi que la gobernanza es explicita: una fuente
/// externa (y sus datasets) SOLO es visible/ejecutable por los tenants con concesion.
///
/// Entidad de PLATAFORMA (NO tenant-scoped): la administra PlatformAdmin y por eso NO lleva el filtro
/// global de consulta. El <see cref="TenantId"/> aqui es el tenant BENEFICIARIO (un Guid logico), no un
/// campo de aislamiento: el catalogo, al servir al tenant activo, cruza en codigo por este TenantId.
/// Guardar la concesion de otro tenant en esta tabla es intencional (es un metadato de plataforma).
/// </summary>
public class ExternalDataSourceGrant : BaseEntity
{
    /// <summary>Fuente externa concedida (FK dura; ambas son de plataforma).</summary>
    public Guid ExternalDataSourceId { get; set; }

    public ExternalDataSource? ExternalDataSource { get; set; }

    /// <summary>Tenant beneficiario de la concesion. NO es campo de aislamiento (esta tabla es global);
    /// es el objetivo de la gobernanza explicita.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Rol dinamico opcional dentro del tenant al que se acota la concesion. Null = todo el
    /// tenant. La aplicacion del rol es responsabilidad de la capa que resuelve el catalogo.</summary>
    public Guid? RolId { get; set; }

    /// <summary>Si la concesion esta vigente.</summary>
    public bool IsEnabled { get; set; } = true;
}
