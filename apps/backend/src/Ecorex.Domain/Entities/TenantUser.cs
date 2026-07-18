using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Vinculo entre un PlatformUser y un tenant, con rol interno. Entidad TENANT-SCOPED:
/// recibe filtro global de consulta por TenantId.
/// </summary>
public class TenantUser : TenantEntity
{
    public Guid PlatformUserId { get; set; }
    public PlatformUser? PlatformUser { get; set; }

    public string Email { get; set; } = null!;
    public TenantRole TenantRole { get; set; } = TenantRole.Advisor;
    public PlatformUserStatus Status { get; set; } = PlatformUserStatus.Active;

    /// <summary>Codigo o documento del asesor/vendedor dentro de este tenant (maestro de vendedores 000124). Opcional.</summary>
    public string? DocumentCode { get; set; }

    /// <summary>Telefono de contacto del asesor/vendedor. Opcional.</summary>
    public string? Phone { get; set; }

    /// <summary>Alcance de leads del asesor (los admin/owner/supervisor ven todo por rol).</summary>
    public LeadVisibility LeadVisibility { get; set; } = LeadVisibility.OwnOnly;

    /// <summary>
    /// Vista del menu configurable asignada a este usuario (Ola 1 del menu por perfil).
    /// Null = usa la vista IsDefault del tenant. FK NO ACTION (no arrastra al usuario por cascada).
    /// </summary>
    public Guid? MenuViewId { get; set; }
    public MenuView? MenuView { get; set; }

    /// <summary>
    /// Rol de permisos (Ola B1) asignado a este usuario. Null = sin rol de permisos finos; el
    /// enforcement (Ola B2) igual permite todo a Owner/Admin por su TenantRole. FK NO ACTION
    /// (borrar un rol no arrastra al usuario; la app bloquea el borrado si tiene usuarios).
    /// Distinto de <see cref="TenantRole"/> (poder organico): ver ADR-0032.
    /// </summary>
    public Guid? RolId { get; set; }
    public Rol? Rol { get; set; }

    /// <summary>Token de invitacion para que el asesor complete su registro (clave + foto). Null si ya activo.</summary>
    public string? InvitationToken { get; set; }
    public DateTimeOffset? InvitationExpiresAt { get; set; }
}
