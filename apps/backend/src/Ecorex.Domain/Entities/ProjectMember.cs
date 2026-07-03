using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// ACL del proyecto: usuario del tenant con acceso al proyecto. CanEdit distingue
/// lectura de edicion (el owner del proyecto siempre puede editar). TENANT-SCOPED.
/// Unico por (ProjectId, TenantUserId).
/// </summary>
public class ProjectMember : TenantEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid TenantUserId { get; set; }
    public TenantUser? TenantUser { get; set; }

    public bool CanEdit { get; set; }
}
