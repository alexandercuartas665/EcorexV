using Ecorex.Application.Common;
using Ecorex.Application.Reporting.External;
using Ecorex.Application.Reporting.Sources;

namespace Ecorex.Application.Reporting;

/// <summary>
/// El CONTRATO CENTRAL del motor: traduce un spec declarativo + el tenant activo en filas ya
/// filtradas. Es el UNICO camino a los datos para el visor/editor y los dashboards; ninguno toca la
/// BD por su cuenta ni recibe una cadena de conexion.
///
/// Devuelve un <see cref="ReportDataSet"/> neutro que alimenta por igual al visor Bold (JSON/Web data
/// source) y a ECharts (series del "option").
/// </summary>
public interface IReportDataSource
{
    Task<ReportDataSet> QueryAsync(ReportQuerySpec spec, ReportContext ctx, CancellationToken ct = default);
}

/// <summary>
/// Despachador y VALIDADOR. Es el choke point de seguridad: (1) resuelve la fuente contra el catalogo,
/// (2) valida que TODO campo referenciado por el spec existe en la fuente y admite la operacion pedida
/// (limite de seguridad: nada fuera del catalogo), (3) delega en la fuente nativa o en el lector de
/// contenedor. El aislamiento tenant lo garantiza el filtro global del DbContext (fail-closed), no la
/// confianza en el ctx.
/// </summary>
public sealed class ReportDataSource : IReportDataSource
{
    private readonly IReportCatalog _catalog;
    private readonly IEnumerable<IReportableSource> _nativeSources;
    private readonly ContainerReportReader _containers;
    private readonly ExternalReportReader _external;
    private readonly ITenantContext _tenantContext;

    public ReportDataSource(
        IReportCatalog catalog,
        IEnumerable<IReportableSource> nativeSources,
        ContainerReportReader containers,
        ExternalReportReader external,
        ITenantContext tenantContext)
    {
        _catalog = catalog;
        _nativeSources = nativeSources;
        _containers = containers;
        _external = external;
        _tenantContext = tenantContext;
    }

    public async Task<ReportDataSet> QueryAsync(ReportQuerySpec spec, ReportContext ctx, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            // Fail-closed: sin tenant activo no hay datos (coherente con el filtro global).
            throw new ReportValidationException("No hay tenant activo: la consulta de reporte queda cerrada.");
        }

        // El ctx del llamador es informativo; la fuente de verdad del tenant es el contexto EF.
        var effectiveCtx = ctx with { TenantId = tenantId };

        var descriptor = await _catalog.GetSourceAsync(spec.SourceKey, ct)
            ?? throw new ReportValidationException($"Fuente reportable desconocida: '{spec.SourceKey}'.");

        ValidateAgainstCatalog(spec, descriptor);

        if (descriptor.Kind == ReportSourceKind.Container)
        {
            return await _containers.QueryAsync(descriptor, spec, effectiveCtx, ct);
        }

        if (descriptor.Kind == ReportSourceKind.External)
        {
            // Fuente externa gobernada (ADR-0064): el reader re-verifica la concesion del tenant, descifra
            // en memoria y ejecuta el dataset curado de solo lectura. El alcance viaja por contexto de
            // confianza; aqui no se admiten filtros libres (los campos externos son CanFilter=false).
            var externalId = ExternalReportReader.ParseId(descriptor.Key)
                ?? throw new ReportValidationException($"Clave externa invalida: '{descriptor.Key}'.");
            var runCtx = new ExternalRunContext(tenantId, effectiveCtx.UserId);
            return await _external.QueryAsync(externalId, runCtx, inputs: null, ct);
        }

        var source = _nativeSources.FirstOrDefault(s =>
            string.Equals(s.Describe().Key, descriptor.Key, StringComparison.OrdinalIgnoreCase))
            ?? throw new ReportValidationException($"No hay proveedor para la fuente nativa '{descriptor.Key}'.");

        return await source.QueryAsync(spec, effectiveCtx, ct);
    }

    /// <summary>
    /// Limite de seguridad: rechaza cualquier referencia a campos que no existan en la fuente o que no
    /// admitan la operacion pedida. Corre ANTES de tocar la BD.
    /// </summary>
    private static void ValidateAgainstCatalog(ReportQuerySpec spec, ReportSourceDescriptor descriptor)
    {
        ReportField Require(string key)
        {
            return descriptor.FindField(key)
                ?? throw new ReportValidationException(
                    $"El campo '{key}' no existe en la fuente '{descriptor.DisplayName}' (no es reportable).");
        }

        foreach (var key in spec.Fields)
        {
            Require(key);
        }

        foreach (var f in spec.Filters)
        {
            var field = Require(f.FieldKey);
            if (!field.CanFilter)
            {
                throw new ReportValidationException($"El campo '{field.DisplayName}' no admite filtro.");
            }
        }

        foreach (var key in spec.GroupBy)
        {
            var field = Require(key);
            if (!field.CanGroup)
            {
                throw new ReportValidationException($"El campo '{field.DisplayName}' no admite agrupacion.");
            }
        }

        foreach (var agg in spec.Aggregates)
        {
            if (agg.Function == ReportAggregateFunction.Count)
            {
                // Count no exige un campo numerico; si referencia uno, basta que exista.
                if (!string.IsNullOrWhiteSpace(agg.FieldKey))
                {
                    Require(agg.FieldKey);
                }

                continue;
            }

            var field = Require(agg.FieldKey);
            if (!field.CanAggregate)
            {
                throw new ReportValidationException(
                    $"El campo '{field.DisplayName}' no es numerico: no admite {agg.Function}.");
            }
        }

        foreach (var s in spec.Sort)
        {
            // El orden puede referirse a un campo de la fuente o a una columna de salida agregada.
            if (descriptor.FindField(s.FieldKey) is not null)
            {
                continue;
            }

            var isAggOutput = s.FieldKey.Equals("Count", StringComparison.OrdinalIgnoreCase)
                || spec.Aggregates.Any(a => $"{a.Function}_{a.FieldKey}".Equals(s.FieldKey, StringComparison.OrdinalIgnoreCase));
            if (!isAggOutput)
            {
                throw new ReportValidationException($"No se puede ordenar por '{s.FieldKey}': no es un campo ni una salida del reporte.");
            }
        }
    }
}
