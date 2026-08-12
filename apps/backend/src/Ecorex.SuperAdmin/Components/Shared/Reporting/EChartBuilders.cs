using System.Globalization;

namespace Ecorex.SuperAdmin.Components.Shared.Reporting;

// Builders REUTILIZABLES de "option" de ECharts (ADR-0066). Antes cada panel a medida (OCS, Tareas,
// SIIGO) armaba sus dicts a mano; se extraen aqui para que los paneles a medida y el renderizador
// GENERICO por spec pinten EXACTAMENTE el mismo grafico. Cada metodo devuelve un Dictionary que
// EChart.razor serializa a JSON. Cultura invariante para que el numero se vea igual en todos lados.
//
// Nota: la matriz cruzada con heatmap NO es un grafico ECharts sino una tabla HTML; su calculo vive en
// PanelDataEngine.Matrix y su pintado en el markup (SpecPanelRenderer / OcsDashboardPanel).

public static class EChartBuilders
{
    public static readonly string[] Palette =
        { "#4f46e5", "#0ea5e9", "#10b981", "#f59e0b", "#ef4444", "#8b5cf6", "#ec4899", "#14b8a6" };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static object Round(double v, int round) => (object)Math.Round(v, round);

    /// <summary>Serie temporal (linea suave con area). Reproduce SiigoVentasDashboardPanel.BuildLine y,
    /// con parametros, el area de "creadas por dia" de ActivitiesDashboardPanel.</summary>
    public static object Line(
        IReadOnlyList<string> cats, IReadOnlyList<double> vals,
        string? color = null, double areaOpacity = 0.18, bool boundaryGap = true,
        int? rotate = 60, int? fontSize = 9, string? interval = "auto",
        int gridLeft = 56, int gridRight = 20, int gridTop = 16, int gridBottom = 60,
        int round = 1, bool rawValues = false)
    {
        var xAxis = new Dictionary<string, object?>
        {
            ["type"] = "category",
            ["data"] = cats.ToArray()
        };
        if (!boundaryGap)
        {
            xAxis["boundaryGap"] = false;
        }

        if (rotate is not null || fontSize is not null || interval is not null)
        {
            var axisLabel = new Dictionary<string, object?>();
            if (rotate is not null) { axisLabel["rotate"] = rotate; }
            if (fontSize is not null) { axisLabel["fontSize"] = fontSize; }
            if (interval is not null) { axisLabel["interval"] = interval; }
            xAxis["axisLabel"] = axisLabel;
        }

        return new Dictionary<string, object?>
        {
            ["color"] = new[] { color ?? Palette[0] },
            ["tooltip"] = new Dictionary<string, object?> { ["trigger"] = "axis" },
            ["grid"] = new Dictionary<string, object?> { ["left"] = gridLeft, ["right"] = gridRight, ["top"] = gridTop, ["bottom"] = gridBottom },
            ["xAxis"] = xAxis,
            ["yAxis"] = new Dictionary<string, object?> { ["type"] = "value" },
            ["series"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "line",
                    ["smooth"] = true,
                    ["showSymbol"] = false,
                    ["areaStyle"] = new Dictionary<string, object?> { ["opacity"] = areaOpacity },
                    ["data"] = rawValues
                        ? vals.Select(v => (object)v).ToArray()
                        : vals.Select(v => Round(v, round)).ToArray()
                }
            }
        };
    }

    /// <summary>Barra vertical (categorias en X). Reproduce OcsDashboardPanel.BuildSoBar y
    /// ActivitiesDashboardPanel.BuildBar segun parametros.</summary>
    public static object VerticalBar(
        IReadOnlyList<string> cats, IReadOnlyList<double> vals,
        string? color = null, int? rotate = null, bool label = false, bool rawValues = true, int round = 1,
        int gridLeft = 40, int gridRight = 16, int gridTop = 16, int gridBottom = 28, int barMaxWidth = 48)
    {
        var xAxis = new Dictionary<string, object?> { ["type"] = "category", ["data"] = cats.ToArray() };
        if (rotate is not null)
        {
            xAxis["axisLabel"] = new Dictionary<string, object?> { ["interval"] = 0, ["rotate"] = rotate };
        }

        var series = new Dictionary<string, object?>
        {
            ["type"] = "bar",
            ["barMaxWidth"] = barMaxWidth,
            ["itemStyle"] = new Dictionary<string, object?> { ["borderRadius"] = new[] { 6, 6, 0, 0 } },
            ["data"] = rawValues ? vals.Select(v => (object)v).ToArray() : vals.Select(v => Round(v, round)).ToArray()
        };
        if (label)
        {
            series["label"] = new Dictionary<string, object?> { ["show"] = true, ["position"] = "top" };
        }

        return new Dictionary<string, object?>
        {
            ["color"] = new[] { color ?? Palette[0] },
            ["tooltip"] = new Dictionary<string, object?> { ["trigger"] = "axis" },
            ["grid"] = new Dictionary<string, object?> { ["left"] = gridLeft, ["right"] = gridRight, ["top"] = gridTop, ["bottom"] = gridBottom },
            ["xAxis"] = xAxis,
            ["yAxis"] = new Dictionary<string, object?> { ["type"] = "value" },
            ["series"] = new object[] { series }
        };
    }

    /// <summary>Barra horizontal top-N (categorias en Y). Las categorias llegan en orden top-primero y se
    /// invierten para que la barra mas larga quede arriba. Reproduce OcsDashboardPanel.BuildTopBar (integer,
    /// gridLeft 170, barMaxWidth 20) y SiigoVentasDashboardPanel.BuildBar (gridLeft 150, barMaxWidth 18,
    /// formatter "{c}").</summary>
    public static object HorizontalBar(
        IReadOnlyList<string> catsTopFirst, IReadOnlyList<double> valsTopFirst,
        string? color = null, int gridLeft = 150, int barMaxWidth = 18,
        bool integer = false, int round = 1, string? labelFormatter = "{c}")
    {
        var cats = catsTopFirst.Reverse().ToArray();
        var vals = valsTopFirst.Reverse().ToList();

        var label = new Dictionary<string, object?> { ["show"] = true, ["position"] = "right" };
        if (labelFormatter is not null)
        {
            label["formatter"] = labelFormatter;
        }

        return new Dictionary<string, object?>
        {
            ["color"] = new[] { color ?? Palette[0] },
            ["tooltip"] = new Dictionary<string, object?> { ["trigger"] = "axis", ["axisPointer"] = new Dictionary<string, object?> { ["type"] = "shadow" } },
            ["grid"] = new Dictionary<string, object?> { ["left"] = gridLeft, ["right"] = 24, ["top"] = 10, ["bottom"] = 24 },
            ["xAxis"] = new Dictionary<string, object?> { ["type"] = "value" },
            ["yAxis"] = new Dictionary<string, object?> { ["type"] = "category", ["data"] = cats },
            ["series"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "bar",
                    ["barMaxWidth"] = barMaxWidth,
                    ["itemStyle"] = new Dictionary<string, object?> { ["borderRadius"] = new[] { 0, 4, 4, 0 } },
                    ["label"] = label,
                    ["data"] = integer
                        ? vals.Select(v => (object)(long)v).ToArray()
                        : vals.Select(v => Round(v, round)).ToArray()
                }
            }
        };
    }

    /// <summary>Dona/pastel. Reproduce las donas de los tres paneles segun parametros (radio, leyenda,
    /// formato de etiqueta). data = pares (nombre, valor).</summary>
    public static object Donut(
        IReadOnlyList<(string Name, double Value)> data,
        string[]? colors = null, string radiusInner = "42%", string radiusOuter = "70%",
        bool legendScroll = true, string? tooltipFormatter = "{b}: {c} ({d}%)",
        string? labelFormatter = null, bool showLabel = false, bool avoidLabelOverlap = false,
        bool integerValues = true)
    {
        var tooltip = new Dictionary<string, object?> { ["trigger"] = "item" };
        if (tooltipFormatter is not null)
        {
            tooltip["formatter"] = tooltipFormatter;
        }

        var legend = new Dictionary<string, object?> { ["bottom"] = 0 };
        if (legendScroll)
        {
            legend["type"] = "scroll";
        }

        var series = new Dictionary<string, object?>
        {
            ["type"] = "pie",
            ["radius"] = new[] { radiusInner, radiusOuter },
            ["itemStyle"] = new Dictionary<string, object?> { ["borderRadius"] = 6, ["borderColor"] = "#fff", ["borderWidth"] = 2 },
            ["data"] = data.Select(d => new Dictionary<string, object?>
            {
                ["name"] = d.Name,
                ["value"] = integerValues ? (object)(long)d.Value : Math.Round(d.Value, 1)
            }).ToArray()
        };
        if (avoidLabelOverlap)
        {
            series["avoidLabelOverlap"] = true;
        }

        if (labelFormatter is not null)
        {
            series["label"] = new Dictionary<string, object?> { ["formatter"] = labelFormatter };
        }
        else if (!showLabel)
        {
            series["label"] = new Dictionary<string, object?> { ["show"] = false };
        }

        return new Dictionary<string, object?>
        {
            ["color"] = colors ?? Palette,
            ["tooltip"] = tooltip,
            ["legend"] = legend,
            ["series"] = new object[] { series }
        };
    }

    /// <summary>Pareto (barra + linea de acumulado % en doble eje). Reproduce
    /// SiigoVentasDashboardPanel.BuildPareto.</summary>
    public static object Pareto(
        IReadOnlyList<string> cats, IReadOnlyList<double> bars, IReadOnlyList<double> cum,
        string barName = "Valor", string cumName = "Acumulado %", string leftAxisName = "",
        int round = 1)
    {
        return new Dictionary<string, object?>
        {
            ["color"] = new[] { Palette[0], Palette[4] },
            ["tooltip"] = new Dictionary<string, object?> { ["trigger"] = "axis", ["axisPointer"] = new Dictionary<string, object?> { ["type"] = "shadow" } },
            ["legend"] = new Dictionary<string, object?> { ["data"] = new[] { barName, cumName }, ["bottom"] = 0 },
            ["grid"] = new Dictionary<string, object?> { ["left"] = 56, ["right"] = 48, ["top"] = 16, ["bottom"] = 90 },
            ["xAxis"] = new Dictionary<string, object?>
            {
                ["type"] = "category",
                ["data"] = cats.ToArray(),
                ["axisLabel"] = new Dictionary<string, object?> { ["rotate"] = 55, ["fontSize"] = 9, ["interval"] = 0 }
            },
            ["yAxis"] = new object[]
            {
                new Dictionary<string, object?> { ["type"] = "value", ["name"] = leftAxisName },
                new Dictionary<string, object?> { ["type"] = "value", ["name"] = "%", ["max"] = 100, ["axisLabel"] = new Dictionary<string, object?> { ["formatter"] = "{value}%" } }
            },
            ["series"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = barName, ["type"] = "bar",
                    ["itemStyle"] = new Dictionary<string, object?> { ["borderRadius"] = new[] { 4, 4, 0, 0 } },
                    ["data"] = bars.Select(v => (object)Math.Round(v, round)).ToArray()
                },
                new Dictionary<string, object?>
                {
                    ["name"] = cumName, ["type"] = "line", ["yAxisIndex"] = 1, ["smooth"] = true, ["symbolSize"] = 5,
                    ["data"] = cum.Select(v => (object)Math.Round(v, round)).ToArray()
                }
            }
        };
    }
}
