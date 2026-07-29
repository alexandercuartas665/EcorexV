using System.Text;

namespace Ecorex.Application.Reporting.Authoring;

/// <summary>
/// Autoria por IA (Ola 4): "te doy una instruccion y creo el reporte". Toma una instruccion en
/// lenguaje natural, consulta el catalogo semantico, pide a la IA un JSON-spec (que referencia SOLO
/// el catalogo), lo VALIDA contra el catalogo (guardrail: nada fuera de lo reportable, sin SQL ni
/// cadena de conexion) y lo ejecuta via el datasource tenant-safe para devolver un dashboard listo.
/// El resultado es un <see cref="ReportSpec"/> guardable y editable por el usuario.
/// </summary>
public interface IReportAuthoringService
{
    Task<ReportAuthoringResult> AuthorAsync(string instruction, CancellationToken ct = default);
}

/// <summary>Resultado de la autoria: el spec generado + el dataset ejecutado + la option de ECharts.</summary>
public sealed record ReportAuthoringResult(
    bool Ok,
    string? Error,
    ReportSpec? Spec,
    ReportDataSet? DataSet,
    object? Option)
{
    public static ReportAuthoringResult Fail(string error) => new(false, error, null, null, null);

    public static ReportAuthoringResult Success(ReportSpec spec, ReportDataSet ds, object? option) =>
        new(true, null, spec, ds, option);
}

/// <summary>
/// Seam sobre el LLM (para poder falsearlo en pruebas). Recibe la instruccion + la descripcion del
/// catalogo y devuelve el JSON crudo del modelo (o un error). La implementacion real resuelve el
/// agente/proveedor del tenant y registra el consumo (AiUsageLog).
/// </summary>
public interface IReportSpecGenerator
{
    Task<ReportGenerationResult> GenerateAsync(string instruction, string catalogText, CancellationToken ct = default);
}

public sealed record ReportGenerationResult(bool Ok, string? RawJson, string? Error);

/// <summary>
/// Arma la descripcion COMPACTA del catalogo para el prompt de la IA. Expone solo nombres logicos,
/// tipos y capacidades (nunca columnas fisicas): es el mismo limite de seguridad que el catalogo.
/// </summary>
public static class ReportCatalogPrompt
{
    public static string Describe(IReadOnlyList<ReportSourceDescriptor> sources)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FUENTES REPORTABLES DISPONIBLES (usa EXACTAMENTE estas claves y campos):");
        foreach (var s in sources)
        {
            sb.Append("- sourceKey: \"").Append(s.Key).Append("\"  (").Append(s.DisplayName).Append(", ").Append(s.Kind).AppendLine(")");
            foreach (var f in s.Fields)
            {
                sb.Append("    * ").Append(f.Key).Append(" [").Append(f.Type).Append(']');
                var caps = new List<string>();
                if (f.CanFilter) { caps.Add("filtrable"); }
                if (f.CanGroup) { caps.Add("agrupable"); }
                if (f.CanAggregate) { caps.Add("agregable"); }
                sb.Append(" (").Append(string.Join(", ", caps)).Append(") - ").AppendLine(f.DisplayName);
            }
        }

        return sb.ToString();
    }
}
