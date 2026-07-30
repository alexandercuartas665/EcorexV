using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Asignacion de un reporte (ReportDefinition) a un Rol dinamico del tenant: gobierna QUE reportes
/// ve cada rol. TENANT-SCOPED (filtro global por TenantId). Regla de visibilidad (en
/// IReportDefinitionService.ListAsync): un usuario ve un reporte si es Owner/Admin, o si alguno de
/// sus roles esta asignado, o si el reporte NO tiene ninguna asignacion (visible para todos, para
/// compatibilidad). Independiente de la matriz Modulo x Accion (esa gobierna el ACCESO a las
/// paginas; esta gobierna la VISIBILIDAD por reporte). Unico por (TenantId, ReportDefinitionId, RolId).
/// </summary>
public class ReportDefinitionRole : TenantEntity
{
    public Guid ReportDefinitionId { get; set; }
    public ReportDefinition? ReportDefinition { get; set; }

    /// <summary>Rol dinamico del tenant (matriz de roles, ADR-0032).</summary>
    public Guid RolId { get; set; }
    public Rol? Rol { get; set; }
}
