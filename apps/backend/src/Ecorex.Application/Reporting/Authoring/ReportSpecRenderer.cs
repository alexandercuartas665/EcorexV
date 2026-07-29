namespace Ecorex.Application.Reporting.Authoring;

/// <summary>
/// Convierte un <see cref="ReportSpec"/> + su <see cref="ReportDataSet"/> en una "option" de ECharts
/// (dashboard). Es el mismo convertidor para la IA y para el usuario. Para Table devuelve null (la UI
/// pinta la tabla directamente del dataset). Mapeo generico: primera columna = eje/categoria/nombre,
/// columnas numericas siguientes = series/valores. Los colores salen de una paleta on-brand.
///
/// El convertidor a RDL (imprimible) queda para la Ola 2 (editor/visor Bold), pendiente de la
/// confirmacion de Docker; aqui solo se cubre el camino de dashboard (ECharts).
/// </summary>
public static class ReportSpecRenderer
{
    private static readonly string[] Palette =
        { "#4f46e5", "#0ea5e9", "#10b981", "#f59e0b", "#ef4444", "#8b5cf6" };

    /// <summary>Devuelve la option de ECharts, o null si el spec pide tabla.</summary>
    public static object? BuildOption(ReportSpec spec, ReportDataSet ds)
    {
        if (spec.Chart == ReportChartKind.Table || ds.Columns.Count == 0)
        {
            return null;
        }

        return spec.Chart switch
        {
            ReportChartKind.Pie => BuildPie(spec, ds),
            ReportChartKind.Line => BuildCartesian(spec, ds, "line"),
            ReportChartKind.Bar => BuildCartesian(spec, ds, "bar"),
            _ => null
        };
    }

    private static int FirstValueColumn(ReportDataSet ds)
    {
        for (var i = 1; i < ds.Columns.Count; i++)
        {
            if (ds.Columns[i].Type is ReportFieldType.Number or ReportFieldType.Decimal)
            {
                return i;
            }
        }

        return ds.Columns.Count > 1 ? 1 : 0;
    }

    private static object BuildPie(ReportSpec spec, ReportDataSet ds)
    {
        var valueCol = FirstValueColumn(ds);
        var data = ds.Rows.Select(r => new Dictionary<string, object?>
        {
            ["name"] = r.Count > 0 ? r[0]?.ToString() ?? "(sin dato)" : "(sin dato)",
            ["value"] = r.Count > valueCol ? r[valueCol] : 0
        }).ToArray();

        return new Dictionary<string, object?>
        {
            ["color"] = Palette,
            ["title"] = TitleBlock(spec.Title),
            ["tooltip"] = new Dictionary<string, object?> { ["trigger"] = "item" },
            ["legend"] = new Dictionary<string, object?> { ["bottom"] = 0 },
            ["series"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "pie",
                    ["radius"] = new[] { "45%", "70%" },
                    ["itemStyle"] = new Dictionary<string, object?> { ["borderRadius"] = 6, ["borderColor"] = "#fff", ["borderWidth"] = 2 },
                    ["data"] = data
                }
            }
        };
    }

    private static object BuildCartesian(ReportSpec spec, ReportDataSet ds, string type)
    {
        var cats = ds.Rows.Select(r => r.Count > 0 ? r[0]?.ToString() ?? "" : "").ToArray();

        // Una serie por cada columna numerica (a partir de la segunda columna).
        var series = new List<object>();
        for (var i = 1; i < ds.Columns.Count; i++)
        {
            if (ds.Columns[i].Type is not (ReportFieldType.Number or ReportFieldType.Decimal))
            {
                continue;
            }

            var idx = i;
            var s = new Dictionary<string, object?>
            {
                ["name"] = ds.Columns[idx].DisplayName,
                ["type"] = type,
                ["data"] = ds.Rows.Select(r => r.Count > idx ? r[idx] : null).ToArray()
            };
            if (type == "line")
            {
                s["smooth"] = true;
                s["areaStyle"] = new Dictionary<string, object?> { ["opacity"] = 0.2 };
                s["showSymbol"] = false;
            }
            else
            {
                s["barMaxWidth"] = 48;
                s["itemStyle"] = new Dictionary<string, object?> { ["borderRadius"] = new[] { 6, 6, 0, 0 } };
            }

            series.Add(s);
        }

        if (series.Count == 0)
        {
            // Sin columna numerica: cae a una serie de conteo por categoria.
            series.Add(new Dictionary<string, object?>
            {
                ["type"] = type,
                ["data"] = ds.Rows.Select(_ => (object)1).ToArray()
            });
        }

        return new Dictionary<string, object?>
        {
            ["color"] = Palette,
            ["title"] = TitleBlock(spec.Title),
            ["tooltip"] = new Dictionary<string, object?> { ["trigger"] = "axis" },
            ["legend"] = new Dictionary<string, object?> { ["bottom"] = 0 },
            ["grid"] = new Dictionary<string, object?> { ["left"] = 44, ["right"] = 16, ["top"] = 48, ["bottom"] = 40 },
            ["xAxis"] = new Dictionary<string, object?> { ["type"] = "category", ["boundaryGap"] = type == "bar", ["data"] = cats },
            ["yAxis"] = new Dictionary<string, object?> { ["type"] = "value" },
            ["series"] = series.ToArray()
        };
    }

    private static Dictionary<string, object?> TitleBlock(string title) => new()
    {
        ["text"] = title,
        ["left"] = "center",
        ["textStyle"] = new Dictionary<string, object?> { ["fontSize"] = 14, ["fontWeight"] = 600 }
    };
}
