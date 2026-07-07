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

    /// <summary>Alcance de leads del asesor (los admin/owner/supervisor ven todo por rol).</summary>
    public LeadVisibility LeadVisibility { get; set; } = LeadVisibility.OwnOnly;

    /// <summary>
    /// Vista del menu configurable asignada a este usuario (Ola 1 del menu por perfil).
    /// Null = usa la vista IsDefault del tenant. FK NO ACTION (no arrastra al usuario por cascada).
    /// </summary>
    public Guid? MenuViewId { get; set; }
    public MenuView? MenuView { get; set; }

    /// <summary>Token de invitacion para que el asesor complete su registro (clave + foto). Null si ya activo.</summary>
    public string? InvitationToken { get; set; }
    public DateTimeOffset? InvitationExpiresAt { get; set; }
}
