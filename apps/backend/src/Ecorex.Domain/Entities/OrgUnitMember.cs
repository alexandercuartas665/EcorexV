using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Miembro de una unidad del organigrama (modulo Dependencias, legacy 000850).
/// Unico por (OrgUnitId, TenantUserId); vive y muere con su unidad (FK cascade).
/// TENANT-SCOPED.
/// </summary>
public class OrgUnitMember : TenantEntity
{
    public Guid OrgUnitId { get; set; }
    public OrgUnit? OrgUnit { get; set; }

    /// <summary>Usuario del tenant asignado a la unidad.</summary>
    public Guid TenantUserId { get; set; }

    /// <summary>Rol funcional dentro de la unidad (ej. "Analista", "Lider tecnico").</summary>
    public string? Role { get; set; }

    /// <summary>
    /// Marca al miembro como jefe / responsable de su unidad. A lo sumo uno por unidad; al
    /// activarlo se sincroniza <see cref="OrgUnit.ResponsibleTenantUserId"/> con este usuario
    /// (fuente del "Encargado" por defecto al crear actividades).
    /// </summary>
    public bool IsResponsible { get; set; }
}
