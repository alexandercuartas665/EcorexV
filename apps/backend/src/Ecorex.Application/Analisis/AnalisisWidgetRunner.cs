using System.Text.Json;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Analisis;

/// <summary>Resultado de ejecutar un widget: la forma a pintar o un error legible (nunca excepcion).</summary>
public sealed record AnalisisWidgetResultado(WidgetShape? Forma, string? Error)
{
    public bool Ok => Error is null && Forma is not null;
}

/// <summary>
/// Ejecuta un widget (ADR-0158 D1): spec del motor de reportes -> <see cref="IReportDataSource"/> -> forma
/// segun el tipo; o indicador curado -> su forma. Un widget roto devuelve error y NO tumba la hoja.
/// </summary>
public interface IAnalisisWidgetRunner
{
    Task<AnalisisWidgetResultado> RunAsync(AnalisisWidgetDto widget, ReportContext ctx, CancellationToken ct = default);
}

public sealed class AnalisisWidgetRunner : IAnalisisWidgetRunner
{
    /// <summary>Barras/dona: categorias visibles antes de agrupar el resto en "Otros".</summary>
    public const int TopCategoriasPorDefecto = 8;

    private readonly IReportDataSource _data;
    private readonly IIndicadorCuradoRegistry _curados;

    public AnalisisWidgetRunner(IReportDataSource data, IIndicadorCuradoRegistry curados)
    {
        _data = data;
        _curados = curados;
    }

    public async Task<AnalisisWidgetResultado> RunAsync(AnalisisWidgetDto widget, ReportContext ctx, CancellationToken ct = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(widget.IndicadorKey))
            {
                var curado = _curados.Find(widget.IndicadorKey);
                if (curado is null)
                {
                    return new(null, "El indicador ya no existe.");
                }

                var forma = await curado.QueryAsync(new IndicadorContexto(ctx), ct);
                return new(Adaptar(forma, widget), null);
            }

            var spec = AnalisisJson.DeserializeSpec(widget.SpecJson);
            if (spec is null)
            {
                return new(null, "La consulta del widget no es valida.");
            }

            var ds = await _data.QueryAsync(spec, ctx, ct);
            return new(AFormar(ds, widget), null);
        }
        catch (ReportValidationException ex)
        {
            return new(null, ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new(null, "No se pudieron cargar los datos del widget.");
        }
    }

    /// <summary>Dataset -> forma segun el tipo visual del widget. Puro, testeable.</summary>
    public static WidgetShape AFormar(ReportDataSet ds, AnalisisWidgetDto widget) => widget.Tipo switch
    {
        AnalisisTipoWidget.Kpi => WidgetShapes.ToEscalar(ds, widget.Titulo),
        AnalisisTipoWidget.Barras or AnalisisTipoWidget.Dona or AnalisisTipoWidget.Semaforo =>
            WidgetShapes.ToCategorias(ds, Top(widget.ConfigJson)),
        AnalisisTipoWidget.Linea or AnalisisTipoWidget.Area => WidgetShapes.ToSerie(ds),
        AnalisisTipoWidget.Heatmap => WidgetShapes.ToHeatmap(ds),
        _ => WidgetShapes.ToTabla(ds)
    };

    /// <summary>Forma de un curado -> lo que pide el tipo (p.ej. KPI sobre un curado de categorias = total).</summary>
    public static WidgetShape Adaptar(WidgetShape forma, AnalisisWidgetDto widget) => (widget.Tipo, forma) switch
    {
        (AnalisisTipoWidget.Kpi, CategoriasShape c) => new EscalarShape(c.Total, widget.Titulo),
        _ => forma
    };

    /// <summary>Lee "top" del ConfigJson (ej. {"top":5}); por defecto <see cref="TopCategoriasPorDefecto"/>.</summary>
    public static int Top(string? configJson)
    {
        if (!string.IsNullOrWhiteSpace(configJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(configJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("top", out var t)
                    && t.TryGetInt32(out var n) && n > 0)
                {
                    return n;
                }
            }
            catch (JsonException)
            {
                // Config corrupta: se usa el valor por defecto.
            }
        }

        return TopCategoriasPorDefecto;
    }
}

/// <summary>
/// Widgets de ejemplo con spec FIJO (ADR-0158 Ola 2, antes del picker de la Ola 3). Cubren las tres
/// fuentes (Tareas, Directorio, Horas) y los dos indicadores curados.
/// </summary>
public static class AnalisisEjemplos
{
    public sealed record Ejemplo(string Key, string Nombre, AnalisisNuevoWidget Widget);

    private static readonly ReportAggregate Conteo = new(string.Empty, ReportAggregateFunction.Count);

    public static readonly IReadOnlyList<Ejemplo> Catalogo = new[]
    {
        new Ejemplo("tareas-abiertas", "KPI - Tareas abiertas", new AnalisisNuevoWidget(
            AnalisisTipoWidget.Kpi, "Tareas abiertas",
            new ReportQuerySpec
            {
                SourceKey = TaskItemReportSource.SourceKey,
                Filters = new[]
                {
                    ReportFilter.Eq("IsArchived", "false"),
                    ReportFilter.In("Status", "Pending", "Active", "InProgress", "Suspended")
                },
                GroupBy = new[] { "Status" },
                Aggregates = new[] { Conteo }
            }, null)),
        new Ejemplo("tareas-por-etapa", "Barras - Tareas por etapa", new AnalisisNuevoWidget(
            AnalisisTipoWidget.Barras, "Tareas por etapa",
            new ReportQuerySpec
            {
                SourceKey = TaskItemReportSource.SourceKey,
                Filters = new[] { ReportFilter.Eq("IsArchived", "false") },
                GroupBy = new[] { "Stage" },
                Aggregates = new[] { Conteo },
                Sort = new[] { new ReportSort("Count", Descending: true) }
            }, null, ColSpan: 2)),
        new Ejemplo("semaforo", "Dona - Semaforo de vencimiento", new AnalisisNuevoWidget(
            AnalisisTipoWidget.Dona, "Semaforo de vencimiento", null, SemaforoVencimientoIndicador.IndicadorKey)),
        new Ejemplo("tiempo-etapa", "Barras - Dias promedio por etapa", new AnalisisNuevoWidget(
            AnalisisTipoWidget.Barras, "Dias promedio en cada etapa", null, TiempoPorEtapaIndicador.IndicadorKey, ColSpan: 2)),
        new Ejemplo("clientes", "KPI - Clientes en el directorio", new AnalisisNuevoWidget(
            AnalisisTipoWidget.Kpi, "Clientes en el directorio",
            new ReportQuerySpec
            {
                SourceKey = TerceroReportSource.SourceKey,
                Filters = new[] { ReportFilter.Eq("EsCliente", "true") },
                GroupBy = new[] { "Tipo" },
                Aggregates = new[] { Conteo }
            }, null)),
        new Ejemplo("directorio-perfil", "Dona - Directorio por perfil", new AnalisisNuevoWidget(
            AnalisisTipoWidget.Dona, "Directorio por perfil",
            new ReportQuerySpec
            {
                SourceKey = TerceroReportSource.SourceKey,
                GroupBy = new[] { "Perfiles" },
                Aggregates = new[] { Conteo },
                Sort = new[] { new ReportSort("Count", Descending: true) }
            }, null)),
        new Ejemplo("horas-total", "KPI - Horas trabajadas", new AnalisisNuevoWidget(
            AnalisisTipoWidget.Kpi, "Horas trabajadas",
            new ReportQuerySpec
            {
                SourceKey = WorkLogReportSource.SourceKey,
                GroupBy = new[] { "Mes" },
                Aggregates = new[] { new ReportAggregate("Horas", ReportAggregateFunction.Sum) }
            }, null)),
        new Ejemplo("horas-usuario", "Barras - Horas por usuario", new AnalisisNuevoWidget(
            AnalisisTipoWidget.Barras, "Horas por usuario",
            new ReportQuerySpec
            {
                SourceKey = WorkLogReportSource.SourceKey,
                GroupBy = new[] { "Usuario" },
                Aggregates = new[] { new ReportAggregate("Horas", ReportAggregateFunction.Sum) },
                Sort = new[] { new ReportSort("Sum_Horas", Descending: true) }
            }, null, ColSpan: 2))
    };

    public static Ejemplo? Find(string key) => Catalogo.FirstOrDefault(e => e.Key == key);
}
