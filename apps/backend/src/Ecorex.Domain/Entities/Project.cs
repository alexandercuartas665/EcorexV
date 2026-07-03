using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Proyecto del tenant: agrupa TaskItems y define un ACL propio (owner + ProjectMember).
/// TENANT-SCOPED, con concurrencia optimista portable (Version, ADR-0013).
/// Codigo unico por tenant (ej. "PRJ-001").
/// </summary>
public class Project : TenantEntity, IVersioned
{
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Planning;

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    /// <summary>Dueno del proyecto (TenantUser): acceso y edicion totales.</summary>
    public Guid OwnerTenantUserId { get; set; }
    public TenantUser? OwnerTenantUser { get; set; }

    /// <summary>Soft-archive: el proyecto sale de las listas pero conserva historia.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Token de concurrencia optimista portable (lo incrementa el interceptor).</summary>
    public long Version { get; set; }
}
