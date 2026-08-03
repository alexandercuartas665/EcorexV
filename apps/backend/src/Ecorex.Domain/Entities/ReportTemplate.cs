using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Plantilla de reporte de PLATAFORMA (ADR-0062): el "molde" reutilizable entre tenants. Entidad
/// GLOBAL (hereda de <see cref="BaseEntity"/>, NO es <see cref="ITenantScoped"/> ni lleva filtro de
/// tenant), administrada desde PlatformAdmin igual que <see cref="PlatformUser"/>, <see cref="SaasPlan"/>
/// o <see cref="ModuleDefinition"/>. NO lleva datos: guarda el SourceKey + spec/panel/RDL. Al activarla
/// en un tenant se crea una instancia <see cref="ReportDefinition"/> tenant-scoped (snapshot + vinculo
/// <see cref="ReportDefinition.TemplateId"/>) que corre via IReportDataSource con el filtro global, asi
/// que compartir la plantilla NUNCA filtra datos entre tenants.
/// </summary>
public class ReportTemplate : BaseEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Dashboard, imprimible o panel (dashboard multi-grafico). Ver <see cref="ReportTemplateKind"/>.</summary>
    public ReportTemplateKind Kind { get; set; } = ReportTemplateKind.Dashboard;

    /// <summary>Clave de la fuente reportable (ej. "native:taskitem", "panel:ocs"). Se copia a la instancia.</summary>
    public string SourceKey { get; set; } = null!;

    /// <summary>JSON-spec declarativo (dashboards). Null para paneles/imprimibles que no lo requieran; la
    /// activacion sintetiza un spec minimo si falta, ya que la instancia lo exige no-null.</summary>
    public string? SpecJson { get; set; }

    /// <summary>RDL del imprimible (Kind=Printable). Null en dashboards/paneles.</summary>
    public string? Rdl { get; set; }

    /// <summary>Naturaleza de la fuente exigida: Native (siempre activable) o Container (requiere contenedor).</summary>
    public ReportTemplateSourceKind RequiredSourceKind { get; set; } = ReportTemplateSourceKind.Native;

    /// <summary>Nombre del contenedor requerido cuando RequiredSourceKind=Container (ej. "Software OCS").
    /// La activacion valida que el tenant tenga un contenedor raiz cuyo nombre coincida.</summary>
    public string? RequiredContainerName { get; set; }

    /// <summary>Categoria de catalogo (agrupacion en la galeria/administracion).</summary>
    public string? Category { get; set; }

    /// <summary>Icono opcional (nombre/emoji) para la tarjeta.</summary>
    public string? Icon { get; set; }

    /// <summary>Solo las publicadas son visibles/activables por los tenants. Las no publicadas son borradores de plataforma.</summary>
    public bool IsPublished { get; set; }
}
