using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Unidad del organigrama del tenant (modulo Dependencias, legacy 000850): area o equipo,
/// en arbol via ParentId (self-FK NO ACTION: una unidad con hijos no se borra por cascada).
/// El servicio valida que el arbol no tenga ciclos (una unidad no puede ser su propio
/// ancestro). Nunca se borra fisicamente: se archiva (IsArchived). TENANT-SCOPED.
/// </summary>
public class OrgUnit : TenantEntity
{
    public string Name { get; set; } = null!;

    public OrgUnitKind Kind { get; set; } = OrgUnitKind.Area;

    /// <summary>Unidad padre (null = raiz del organigrama).</summary>
    public Guid? ParentId { get; set; }
    public OrgUnit? Parent { get; set; }

    /// <summary>Responsable de la unidad (TenantUser del mismo tenant, opcional).</summary>
    public Guid? ResponsibleTenantUserId { get; set; }

    public string? Description { get; set; }

    /// <summary>Orden entre hermanos dentro del mismo padre.</summary>
    public int SortOrder { get; set; }

    public bool IsArchived { get; set; }
}
