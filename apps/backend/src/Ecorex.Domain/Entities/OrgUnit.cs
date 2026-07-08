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

    /// <summary>
    /// Clasificador de asignacion por nodo (ADR-0035): Dependencia contiene Cargos y un
    /// Cargo contiene Funcionarios (via ParentId). Default Dependencia para filas heredadas.
    /// </summary>
    public OrgUnitClassifier Classifier { get; set; } = OrgUnitClassifier.Dependencia;

    /// <summary>
    /// Usuario del tenant (TenantUser.Id) que ocupa este puesto. SOLO se usa cuando
    /// Classifier=Funcionario; null para Dependencia y Cargo. FK NO ACTION (el usuario
    /// nunca se borra en cascada desde el organigrama).
    /// </summary>
    public Guid? TenantUserId { get; set; }

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
