using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Analisis;

/// <summary>
/// Indicador CURADO (ADR-0158 D1): calculo que un <see cref="ReportQuerySpec"/> no expresa solo
/// (cubetas por fecha relativa, promedios por etapa, delta). Devuelve las MISMAS formas que un spec y
/// consulta SIEMPRE a traves de <see cref="IReportDataSource"/> (validacion de catalogo + tenant).
/// </summary>
public interface IIndicadorCurado
{
    /// <summary>Clave estable que guarda el widget (ej. "tareas.semaforo-vencimiento").</summary>
    string Key { get; }

    string Nombre { get; }

    string Descripcion { get; }

    /// <summary>Fuente base sobre la que se aplican los filtros de contexto (hoja, ficha embebida).</summary>
    string SourceKey { get; }

    Task<WidgetShape> QueryAsync(IndicadorContexto contexto, CancellationToken ct = default);
}

/// <summary>
/// Contexto de ejecucion: filtros extra (filtros globales de la hoja o del modulo anfitrion, sobre
/// campos de <see cref="IIndicadorCurado.SourceKey"/>) y el instante "ahora" (inyectable para tests).
/// </summary>
public sealed record IndicadorContexto(ReportContext Report, IReadOnlyList<ReportFilter>? Filtros = null, DateTimeOffset? Ahora = null)
{
    public IReadOnlyList<ReportFilter> FiltrosEfectivos => Filtros ?? Array.Empty<ReportFilter>();

    public DateTimeOffset AhoraEfectivo => Ahora ?? DateTimeOffset.UtcNow;
}

/// <summary>Busqueda de indicadores curados registrados por DI.</summary>
public interface IIndicadorCuradoRegistry
{
    IReadOnlyList<IIndicadorCurado> All { get; }

    IIndicadorCurado? Find(string key);
}

public sealed class IndicadorCuradoRegistry : IIndicadorCuradoRegistry
{
    public IndicadorCuradoRegistry(IEnumerable<IIndicadorCurado> indicadores) => All = indicadores.ToList();

    public IReadOnlyList<IIndicadorCurado> All { get; }

    public IIndicadorCurado? Find(string key) =>
        All.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Semaforo de vencimiento de tareas abiertas (no archivadas, no terminadas): Vencidas / Vencen en 7
/// dias / Al dia / Sin fecha. Forma: <see cref="CategoriasShape"/> con tono por cubeta.
/// </summary>
public sealed class SemaforoVencimientoIndicador : IIndicadorCurado
{
    public const string IndicadorKey = "tareas.semaforo-vencimiento";
    public const int DiasPorVencer = 7;

    private readonly IReportDataSource _data;

    public SemaforoVencimientoIndicador(IReportDataSource data) => _data = data;

    public string Key => IndicadorKey;
    public string Nombre => "Semaforo de vencimiento";
    public string Descripcion => "Tareas abiertas: vencidas, por vencer (7 dias), al dia y sin fecha.";
    public string SourceKey => TaskItemReportSource.SourceKey;

    public async Task<WidgetShape> QueryAsync(IndicadorContexto contexto, CancellationToken ct = default)
    {
        var spec = new ReportQuerySpec
        {
            SourceKey = SourceKey,
            Fields = new[] { "DueDate", "Status", "ClosedAt" },
            Filters = new[] { ReportFilter.Eq("IsArchived", "false") }.Concat(contexto.FiltrosEfectivos).ToList()
        };

        var ds = await _data.QueryAsync(spec, contexto.Report, ct);
        return Clasificar(ds.Rows.Select(r => (r[0] as DateTimeOffset?, r[1] as string, r[2] as DateTimeOffset?)), contexto.AhoraEfectivo);
    }

    /// <summary>Clasificacion pura (testeable): ignora terminadas (Done/Closed o con cierre).</summary>
    public static CategoriasShape Clasificar(IEnumerable<(DateTimeOffset? Due, string? Status, DateTimeOffset? ClosedAt)> rows, DateTimeOffset ahora)
    {
        int vencidas = 0, porVencer = 0, alDia = 0, sinFecha = 0;
        var limite = ahora.AddDays(DiasPorVencer);
        foreach (var (due, status, closedAt) in rows)
        {
            var terminada = closedAt is not null
                || string.Equals(status, nameof(TaskItemStatus.Done), StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, nameof(TaskItemStatus.Closed), StringComparison.OrdinalIgnoreCase);
            if (terminada)
            {
                continue;
            }

            if (due is null) { sinFecha++; }
            else if (due < ahora) { vencidas++; }
            else if (due <= limite) { porVencer++; }
            else { alDia++; }
        }

        return new CategoriasShape(new[]
        {
            new CategoriaItem("Vencidas", vencidas, "rose"),
            new CategoriaItem("Por vencer", porVencer, "amber"),
            new CategoriaItem("Al dia", alDia, "green"),
            new CategoriaItem("Sin fecha", sinFecha, "slate")
        });
    }
}

/// <summary>
/// Tiempo promedio (dias) que las tareas abiertas llevan en su etapa actual, por etapa. Forma:
/// <see cref="CategoriasShape"/> ordenada de mayor a menor (cuellos de botella primero).
/// </summary>
public sealed class TiempoPorEtapaIndicador : IIndicadorCurado
{
    public const string IndicadorKey = "tareas.tiempo-por-etapa";

    private readonly IReportDataSource _data;

    public TiempoPorEtapaIndicador(IReportDataSource data) => _data = data;

    public string Key => IndicadorKey;
    public string Nombre => "Tiempo promedio por etapa";
    public string Descripcion => "Dias promedio que las tareas abiertas llevan en su etapa actual.";
    public string SourceKey => TaskItemReportSource.SourceKey;

    public async Task<WidgetShape> QueryAsync(IndicadorContexto contexto, CancellationToken ct = default)
    {
        var spec = new ReportQuerySpec
        {
            SourceKey = SourceKey,
            Filters = new[] { ReportFilter.Eq("IsArchived", "false") }.Concat(contexto.FiltrosEfectivos).ToList(),
            GroupBy = new[] { "Stage" },
            Aggregates = new[] { new ReportAggregate("DiasEnEtapa", ReportAggregateFunction.Avg) }
        };

        var ds = await _data.QueryAsync(spec, contexto.Report, ct);

        // Las terminadas tienen DiasEnEtapa nulo -> su promedio es null y se descartan.
        var items = ds.Rows
            .Where(r => r[1] is not null)
            .Select(r => new CategoriaItem(
                r[0] as string is { Length: > 0 } s ? s : WidgetShapes.SinDato,
                Math.Round(InMemoryReportEngine.ToDecimal(r[1]) ?? 0m, 1)))
            .OrderByDescending(i => i.Valor)
            .ToList();
        return new CategoriasShape(items);
    }
}
