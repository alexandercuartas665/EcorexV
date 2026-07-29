using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Definicion de un reporte guardado por el tenant (Motor de Reportes y BI, ADR-0051). Es el
/// ARTEFACTO COMPARTIDO entre la IA (que lo genera por instruccion, Ola 4) y el usuario (que lo abre
/// y ajusta): vive como DATO, no como codigo. Para dashboards guarda <see cref="SpecJson"/> (el
/// JSON-spec declarativo que referencia SOLO el catalogo semantico); para imprimibles guardara
/// tambien <see cref="Rdl"/> (Ola 2, editor Bold). TENANT-SCOPED + concurrencia optimista. Siguiendo
/// la norma del proyecto, NO hay soft-delete: se archiva con <see cref="Status"/>.
/// </summary>
public class ReportDefinition : TenantEntity, IVersioned
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Dashboard interactivo (ECharts) o documento imprimible (RDL/Bold).</summary>
    public ReportDefinitionKind Kind { get; set; } = ReportDefinitionKind.Dashboard;

    public ReportDefinitionStatus Status { get; set; } = ReportDefinitionStatus.Active;

    /// <summary>Clave de la fuente reportable del catalogo (ej. "native:taskitem" o "container:{guid}").
    /// Redundante con el spec, pero util para listar/filtrar sin deserializar.</summary>
    public string? SourceKey { get; set; }

    /// <summary>El JSON-spec declarativo (titulo + consulta + presentacion). Lo genera la IA o el editor.</summary>
    public string SpecJson { get; set; } = null!;

    /// <summary>RDL del reporte imprimible (Ola 2, editor Bold). Null para dashboards.</summary>
    public string? Rdl { get; set; }

    /// <summary>Token de concurrencia optimista portable (lo incrementa el interceptor, ADR-0013).</summary>
    public long Version { get; set; }
}
