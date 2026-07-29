using Ecorex.Application.Common;

namespace Ecorex.Application.Reporting.Authoring;

/// <summary>
/// Orquesta la autoria por IA (Ola 4). Pipeline determinista alrededor de la unica pieza no
/// determinista (el LLM, detras de <see cref="IReportSpecGenerator"/>):
/// 1. describe el catalogo (limite de seguridad),
/// 2. pide el JSON-spec al generador,
/// 3. extrae y deserializa el JSON de forma tolerante,
/// 4. lo VALIDA + ejecuta via el datasource tenant-safe (un campo fuera del catalogo se rechaza),
/// 5. arma la option de ECharts.
/// La IA nunca ve ni escribe SQL/columnas fisicas: solo referencia el catalogo.
/// </summary>
public sealed class ReportAuthoringService : IReportAuthoringService
{
    private readonly IReportCatalog _catalog;
    private readonly IReportDataSource _dataSource;
    private readonly IReportSpecGenerator _generator;
    private readonly ITenantContext _tenantContext;

    public ReportAuthoringService(
        IReportCatalog catalog,
        IReportDataSource dataSource,
        IReportSpecGenerator generator,
        ITenantContext tenantContext)
    {
        _catalog = catalog;
        _dataSource = dataSource;
        _generator = generator;
        _tenantContext = tenantContext;
    }

    public async Task<ReportAuthoringResult> AuthorAsync(string instruction, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instruction))
        {
            return ReportAuthoringResult.Fail("Escribe una instruccion para el reporte.");
        }

        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return ReportAuthoringResult.Fail("No hay tenant activo.");
        }

        var sources = await _catalog.GetSourcesAsync(ct);
        if (sources.Count == 0)
        {
            return ReportAuthoringResult.Fail("No hay fuentes reportables en el tenant.");
        }

        var catalogText = ReportCatalogPrompt.Describe(sources);

        var gen = await _generator.GenerateAsync(instruction, catalogText, ct);
        if (!gen.Ok || string.IsNullOrWhiteSpace(gen.RawJson))
        {
            return ReportAuthoringResult.Fail(gen.Error ?? "La IA no devolvio una respuesta.");
        }

        var json = ExtractJson(gen.RawJson);
        var spec = json is null ? null : ReportSpec.FromJson(json);
        if (spec is null || string.IsNullOrWhiteSpace(spec.SourceKey))
        {
            return ReportAuthoringResult.Fail("La IA no devolvio un JSON-spec valido (falta la fuente).");
        }

        ReportDataSet ds;
        try
        {
            // Validacion + ejecucion en un solo paso: el datasource rechaza cualquier campo/fuente
            // fuera del catalogo (guardrail) y, si es valido, devuelve las filas ya filtradas por tenant.
            ds = await _dataSource.QueryAsync(spec.ToQuerySpec(), new ReportContext(tenantId), ct);
        }
        catch (ReportValidationException ex)
        {
            return ReportAuthoringResult.Fail("El reporte generado referencia algo fuera del catalogo: " + ex.Message);
        }

        var option = ReportSpecRenderer.BuildOption(spec, ds);
        return ReportAuthoringResult.Success(spec, ds, option);
    }

    /// <summary>
    /// Extrae el objeto JSON de la respuesta del modelo, tolerando fences ```json y texto alrededor:
    /// toma del primer '{' al ultimo '}'. Mismo patron que el resto del proyecto (WorkflowAgentDecisionParser).
    /// </summary>
    public static string? ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return raw.Substring(start, end - start + 1);
    }
}
