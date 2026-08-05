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

    /// <summary>
    /// Vinculo de datos EXTERNOS (ADR-0063). JSON con el mapeo de cada dataset del RDL a un
    /// <see cref="ExternalDataSet"/> concedido + los valores de entrada guardados. Null = reporte que
    /// se alimenta por el datasource tenant-safe (camino normal). Cuando no es null, el visor NO ejecuta
    /// la conexion del RDL: el conector externo produce las tablas ya filtradas y se inyectan en memoria.
    /// El SECRETO nunca vive aqui: solo referencia por Id al catalogo de plataforma.
    /// </summary>
    public string? ExternalBindingJson { get; set; }

    /// <summary>
    /// Vinculo a la plantilla de plataforma (ADR-0062) de la que se activo esta instancia. Null =
    /// reporte propio del tenant (creado con IA/editor). NO es una FK dura: <see cref="ReportTemplate"/>
    /// es una entidad GLOBAL y esta instancia es tenant-scoped, asi que el vinculo se guarda como Guid
    /// logico (la app resuelve la plantilla por Id). El dato jamas viaja con la plantilla.
    /// </summary>
    public Guid? TemplateId { get; set; }

    /// <summary>Token de concurrencia optimista portable (lo incrementa el interceptor, ADR-0013).</summary>
    public long Version { get; set; }
}
