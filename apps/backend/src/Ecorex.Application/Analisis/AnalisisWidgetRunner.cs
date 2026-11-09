using System.Text.Json;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Analisis;

/// <summary>Resultado de ejecutar un widget: la forma a pintar o un error legible (nunca excepcion).</summary>
/// <param name="FueraDeContexto">El anfitrion paso un contexto (p.ej. un tercero) que este widget no pudo
/// aplicar a su fuente: muestra datos GENERALES, no los del registro. La tarjeta lo advierte.</param>
public sealed record AnalisisWidgetResultado(WidgetShape? Forma, string? Error, bool FueraDeContexto = false)
{
    public bool Ok => Error is null && Forma is not null;
}

/// <summary>
/// Ejecuta un widget (ADR-0158 D1): spec del motor de reportes -> <see cref="IReportDataSource"/> -> forma
/// segun el tipo; o indicador curado -> su forma. Un widget roto devuelve error y NO tumba la hoja.
/// </summary>
public interface IAnalisisWidgetRunner
{
    /// <param name="hoja">Filtros globales de la hoja (null = sin filtros). Cada widget los aplica sobre su fuente.</param>
    /// <param name="contexto">Parametros del modulo anfitrion cuando el widget va embebido (ADR-0158 D9).</param>
    Task<AnalisisWidgetResultado> RunAsync(
        AnalisisWidgetDto widget, ReportContext ctx, CancellationToken ct = default, HojaFiltros? hoja = null,
        IReadOnlyDictionary<string, string>? contexto = null);
}

public sealed class AnalisisWidgetRunner : IAnalisisWidgetRunner
{
    /// <summary>Barras/dona: categorias visibles antes de agrupar el resto en "Otros".</summary>
    public const int TopCategoriasPorDefecto = 8;

    private readonly IReportDataSource _data;
    private readonly IIndicadorCuradoRegistry _curados;
    private readonly IReportCatalog? _catalog;

    public AnalisisWidgetRunner(IReportDataSource data, IIndicadorCuradoRegistry curados, IReportCatalog? catalog = null)
    {
        _data = data;
        _curados = curados;
        _catalog = catalog;
    }

    public async Task<AnalisisWidgetResultado> RunAsync(
        AnalisisWidgetDto widget, ReportContext ctx, CancellationToken ct = default, HojaFiltros? hoja = null,
        IReadOnlyDictionary<string, string>? contexto = null)
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

                var (extra, fuera) = await FiltrosExtraAsync(curado.SourceKey, hoja, contexto, widget.ContextBindingsJson, ct);
                var forma = await curado.QueryAsync(new IndicadorContexto(ctx, extra), ct);
                return new(Adaptar(forma, widget), null, fuera);
            }

            var spec = AnalisisJson.DeserializeSpec(widget.SpecJson);
            if (spec is null)
            {
                return new(null, "La consulta del widget no es valida.");
            }

            var (filtrosHoja, fueraDeContexto) = await FiltrosExtraAsync(spec.SourceKey, hoja, contexto, widget.ContextBindingsJson, ct);
            if (filtrosHoja.Count > 0)
            {
                spec = spec with { Filters = spec.Filters.Concat(filtrosHoja).ToList() };
            }

            // Agregado SIN dimension (KPI "total"): casi todas las fuentes exigen exactamente un grupo, asi
            // que se pide el campo en modo tabular (con el mismo tope de filas) y se agrega aqui.
            if (spec.GroupBy.Count == 0 && spec.Aggregates.Count == 1)
            {
                var valor = await AgregarSinGrupoAsync(spec, ctx, ct);
                return new(Adaptar(new EscalarShape(valor, widget.Titulo), widget), null, fueraDeContexto);
            }

            var ds = await _data.QueryAsync(spec, ctx, ct);
            return new(Adaptar(AFormar(ds, widget), widget), null, fueraDeContexto);
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

    /// <summary>
    /// Forma -> lo que pide el tipo (p.ej. KPI sobre un curado de categorias = total) y tono del KPI
    /// segun los umbrales del ConfigJson.
    /// </summary>
    public static WidgetShape Adaptar(WidgetShape forma, AnalisisWidgetDto widget)
    {
        var adaptada = (widget.Tipo, forma) switch
        {
            (AnalisisTipoWidget.Kpi, CategoriasShape c) => new EscalarShape(c.Total, widget.Titulo),
            _ => forma
        };

        if (adaptada is EscalarShape kpi && WidgetConfig.Leer(widget.ConfigJson).TonoPorUmbral(kpi.Valor) is string tono)
        {
            return kpi with { Tono = tono };
        }

        return adaptada;
    }

    /// <summary>Lee "top" del ConfigJson (ej. {"top":5}); por defecto <see cref="TopCategoriasPorDefecto"/>.</summary>
    public static int Top(string? configJson) =>
        WidgetConfig.Leer(configJson).Top is int n and > 0 ? n : TopCategoriasPorDefecto;

    /// <summary>Agrega en memoria los valores de un campo (Count/Sum/Avg/Min/Max), ignorando nulos. Puro.</summary>
    public static decimal? Agregar(ReportAggregateFunction fn, IEnumerable<object?> valores)
    {
        if (fn == ReportAggregateFunction.Count)
        {
            return valores.Count();
        }

        var nums = valores.Select(InMemoryReportEngine.ToDecimal).Where(v => v is not null).Select(v => v!.Value).ToList();
        if (nums.Count == 0)
        {
            return fn == ReportAggregateFunction.Sum ? 0m : null;
        }

        return fn switch
        {
            ReportAggregateFunction.Sum => nums.Sum(),
            ReportAggregateFunction.Avg => Math.Round(nums.Average(), 4),
            ReportAggregateFunction.Min => nums.Min(),
            ReportAggregateFunction.Max => nums.Max(),
            _ => null
        };
    }

    private async Task<decimal?> AgregarSinGrupoAsync(ReportQuerySpec spec, ReportContext ctx, CancellationToken ct)
    {
        var agg = spec.Aggregates[0];
        var campo = agg.Function == ReportAggregateFunction.Count || string.IsNullOrWhiteSpace(agg.FieldKey)
            ? await CampoCualquieraAsync(spec, ct)
            : agg.FieldKey;
        var ds = await _data.QueryAsync(spec with { Fields = new[] { campo }, Aggregates = Array.Empty<ReportAggregate>(), Sort = Array.Empty<ReportSort>() }, ctx, ct);
        return Agregar(agg.Function, ds.Rows.Select(r => r.Count > 0 ? r[0] : null));
    }

    /// <summary>Para contar filas basta proyectar un campo: el primero de la fuente (o el del primer filtro).</summary>
    private async Task<string> CampoCualquieraAsync(ReportQuerySpec spec, CancellationToken ct)
    {
        if (_catalog is not null && await _catalog.GetSourceAsync(spec.SourceKey, ct) is { Fields.Count: > 0 } d)
        {
            return d.Fields[0].Key;
        }

        return spec.Filters.FirstOrDefault()?.FieldKey
            ?? throw new ReportValidationException("No se pudo determinar un campo para contar filas.");
    }

    /// <summary>Filtros de la hoja + filtros del contexto de embebido, sobre los campos de la fuente.</summary>
    private async Task<(IReadOnlyList<ReportFilter> Filtros, bool FueraDeContexto)> FiltrosExtraAsync(
        string sourceKey, HojaFiltros? hoja, IReadOnlyDictionary<string, string>? contexto, string? bindingsJson, CancellationToken ct)
    {
        var hayHoja = hoja is { Vacio: false };
        var hayContexto = contexto is { Count: > 0 };
        if ((!hayHoja && !hayContexto) || _catalog is null)
        {
            return (Array.Empty<ReportFilter>(), hayContexto);
        }

        var fuente = await _catalog.GetSourceAsync(sourceKey, ct);
        if (fuente is null)
        {
            return (Array.Empty<ReportFilter>(), hayContexto);
        }

        var filtros = hayHoja ? HojaFiltroBinder.Filtros(hoja, fuente, bindingsJson).ToList() : new List<ReportFilter>();
        var (delContexto, ignorado) = AnalisisContexto.Filtros(contexto, fuente, bindingsJson);
        filtros.AddRange(delContexto);
        return (filtros, ignorado);
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
