using System.Globalization;
using Ecorex.Domain.Enums;
using O = System.Collections.Generic.Dictionary<string, object?>;

namespace Ecorex.Application.Analisis;

/// <summary>
/// Forma de un widget -> "option" de Apache ECharts (ADR-0158 Ola 7a/7b). PURO (sin interop ni BD): la
/// tarjeta solo entrega este diccionario al componente de grafico. Convenciones que resuelve el JS
/// (wwwroot/js/analisis-graficos.js) porque JSON no lleva funciones ni lee CSS:
/// <list type="bullet">
/// <item>Colores como tokens del prototipo: <c>"var(--t-blue)"</c>; <c>"var(--t-blue)/0.4"</c> = con alfa.
/// Se resuelven contra el tema ACTIVO (claro/oscuro) y se repintan si cambia.</item>
/// <item><c>__fmt</c> en la raiz = formato de los numeros (numero|decimal|moneda|porcentaje) para tooltip,
/// ejes de valor y etiquetas marcadas con <c>__fmt: true</c>.</item>
/// <item><c>__kind</c> en la raiz = formateadores especiales (pie, funnel, treemap, pareto, calendar, spark).</item>
/// </list>
/// KPI clasico, semaforo y tabla NO son graficos: devuelven null y la tarjeta los pinta en HTML.
/// </summary>
public static class AnalisisGraficos
{
    /// <summary>Paleta del workspace (tokens --t-*), en orden de asignacion.</summary>
    public static readonly string[] Tonos = { "blue", "violet", "green", "amber", "rose", "slate" };

    private const string Tinta = "var(--ink)";
    private const string Tinta2 = "var(--ink-2)";
    private const string Tinta3 = "var(--ink-3)";
    private const string Linea2 = "var(--line-2)";
    private const string Superficie = "var(--surface)";
    private const string Superficie3 = "var(--surface-3)";

    private static readonly string[] MesesCortos = { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };

    /// <summary>true si el tipo se pinta con ECharts (el resto: HTML de la tarjeta).</summary>
    public static bool EsGrafico(AnalisisTipoWidget tipo) =>
        tipo is not (AnalisisTipoWidget.Kpi or AnalisisTipoWidget.Semaforo or AnalisisTipoWidget.Tabla or AnalisisTipoWidget.Echarts);

    /// <summary>Color del tono i-esimo de la paleta, empezando por el tono elegido.</summary>
    public static string Color(string? tonoBase, int i) => $"var(--t-{TonoDesde(tonoBase, i)})";

    public static string TonoDesde(string? tonoBase, int i)
    {
        var inicio = Math.Max(0, Array.IndexOf(Tonos, tonoBase ?? "blue"));
        return Tonos[(inicio + i) % Tonos.Length];
    }

    /// <summary>
    /// Option de ECharts para la forma y el tipo, o null si no es un grafico (o la forma no le sirve al tipo).
    /// </summary>
    /// <param name="formatear">Formato de numeros ya resuelto (para textos fijos: total de la dona, meta).</param>
    public static O? Opcion(WidgetShape forma, AnalisisTipoWidget tipo, WidgetConfig cfg, Func<decimal?, string> formatear)
    {
        var o = (tipo, forma) switch
        {
            (AnalisisTipoWidget.Velocimetro, EscalarShape k) => Velocimetro(k, cfg, formatear),
            (AnalisisTipoWidget.Dona, CategoriasShape c) when c.Items.Count > 0 => Dona(c, cfg, formatear),
            (AnalisisTipoWidget.Embudo, CategoriasShape c) when c.Items.Count > 0 => Embudo(c, cfg),
            (AnalisisTipoWidget.Treemap, CategoriasShape c) when c.Items.Count > 0 => Treemap(c, cfg),
            (AnalisisTipoWidget.Treemap, SerieShape s) when s.Ejes.Count > 0 => Treemap(s, cfg),
            (AnalisisTipoWidget.BarrasHorizontales, CategoriasShape c) when c.Items.Count > 0 => Barras(c, cfg, horizontal: true),
            (AnalisisTipoWidget.BarrasHorizontales, SerieShape s) when s.Ejes.Count > 0 => BarrasAgrupadas(s, cfg, horizontal: true, apiladas: false),
            (AnalisisTipoWidget.BarrasApiladas, SerieShape s) when s.Ejes.Count > 0 => BarrasAgrupadas(s, cfg, horizontal: false, apiladas: true),
            (AnalisisTipoWidget.BarrasLinea, CategoriasShape c) when c.Items.Count > 0 => Pareto(c, cfg),
            (AnalisisTipoWidget.BarrasLinea, SerieShape s) when s.Ejes.Count > 0 => BarrasYLinea(s, cfg),
            (AnalisisTipoWidget.Linea or AnalisisTipoWidget.Area, SerieShape s) when s.Ejes.Count > 0 => Lineas(s, cfg, tipo == AnalisisTipoWidget.Area),
            (AnalisisTipoWidget.Heatmap, HeatmapShape h) when h.Filas.Count > 0 => MapaDeCalor(h, cfg),
            (AnalisisTipoWidget.Calendario, CalendarioShape cal) => Calendario(cal, cfg),
            (_, SerieShape s) when s.Ejes.Count > 0 && tipo is AnalisisTipoWidget.Barras or AnalisisTipoWidget.BarrasApiladas =>
                BarrasAgrupadas(s, cfg, horizontal: false, apiladas: tipo == AnalisisTipoWidget.BarrasApiladas),
            (AnalisisTipoWidget.Barras or AnalisisTipoWidget.BarrasApiladas, CategoriasShape c) when c.Items.Count > 0 => Barras(c, cfg, horizontal: false),
            _ => null
        };

        if (o is not null)
        {
            o["__fmt"] = cfg.Formato ?? "numero";
            // Con baseOption (graficos responsivos) las claves de animacion van dentro de la base.
            var destino = o.TryGetValue("baseOption", out var b) && b is O baseOption ? baseOption : o;
            destino.TryAdd("animationDuration", 650);
            destino.TryAdd("animationEasing", "cubicOut");
        }

        return o;
    }

    /// <summary>Mini linea de tendencia del KPI (sin ejes), o null si no hay al menos 2 puntos.</summary>
    public static O? Tendencia(EscalarShape kpi, WidgetConfig cfg)
    {
        if (kpi.Tendencia.Count < 2)
        {
            return null;
        }

        var color = kpi.Tono is string t ? $"var(--t-{t})" : Color(cfg.Tono, 0);
        return new O
        {
            ["__fmt"] = cfg.Formato ?? "numero",
            ["__kind"] = "spark",
            ["grid"] = new O { ["left"] = 2, ["right"] = 2, ["top"] = 6, ["bottom"] = 2 },
            ["tooltip"] = Tooltip("axis", sombra: false),
            ["xAxis"] = new O { ["type"] = "category", ["show"] = false, ["boundaryGap"] = false, ["data"] = kpi.Tendencia.Select(p => p.Etiqueta).ToList() },
            ["yAxis"] = new O { ["type"] = "value", ["show"] = false, ["scale"] = true },
            ["series"] = new object[]
            {
                new O
                {
                    ["type"] = "line",
                    ["name"] = kpi.Etiqueta,
                    ["data"] = kpi.Tendencia.Select(p => p.Valor).ToList(),
                    ["smooth"] = true,
                    ["smoothMonotone"] = "x",
                    ["symbol"] = "circle",
                    ["symbolSize"] = 5,
                    ["showSymbol"] = false,
                    ["lineStyle"] = new O { ["width"] = 2, ["color"] = color },
                    ["itemStyle"] = new O { ["color"] = color },
                    ["areaStyle"] = new O { ["color"] = Degradado(color, 0.28, 0.02) }
                }
            },
            ["animationDuration"] = 650
        };
    }

    /// <summary>Alto sugerido (px) del grafico segun la forma.</summary>
    public static int Alto(WidgetShape forma, AnalisisTipoWidget tipo) => (tipo, forma) switch
    {
        (_, CalendarioShape) => 190,
        (AnalisisTipoWidget.Velocimetro, _) => 200,
        (AnalisisTipoWidget.BarrasHorizontales, CategoriasShape c) => Math.Clamp((c.Items.Count * 30) + 30, 160, 420),
        (AnalisisTipoWidget.BarrasHorizontales, SerieShape s) => Math.Clamp((s.Ejes.Count * ((14 * s.Series.Count) + 14)) + 60, 160, 420),
        (_, HeatmapShape h) => Math.Clamp((h.Filas.Count * 30) + 60, 160, 420),
        (AnalisisTipoWidget.Dona, _) => 210,
        (AnalisisTipoWidget.Treemap, _) => 250,
        _ => 230
    };

    // ---------------------------------------------------------------- barras

    private static O Barras(CategoriasShape c, WidgetConfig cfg, bool horizontal)
    {
        var baseColor = cfg.Tono is string t ? $"var(--t-{t})" : "var(--brand)";
        var etiquetas = c.Items.Select(i => i.Etiqueta).ToList();
        var datos = c.Items.Select(i => (object?)(i.Tono is null
            ? i.Valor
            : new O { ["value"] = i.Valor, ["itemStyle"] = new O { ["color"] = $"var(--t-{i.Tono})" } })).ToList();

        var serie = new O
        {
            ["type"] = "bar",
            ["name"] = "Valor",
            ["data"] = datos,
            ["barMaxWidth"] = horizontal ? 22 : 44,
            ["itemStyle"] = new O
            {
                ["borderRadius"] = horizontal ? new[] { 0, 8, 8, 0 } : new[] { 8, 8, 0, 0 },
                ["color"] = horizontal ? DegradadoH(baseColor, 0.5, 1) : Degradado(baseColor, 1, 0.5)
            },
            ["emphasis"] = new O { ["focus"] = "self" },
            ["label"] = new O
            {
                ["show"] = c.Items.Count <= (horizontal ? 20 : 12),
                ["position"] = horizontal ? "right" : "top",
                ["color"] = Tinta2,
                ["fontSize"] = 11,
                ["fontWeight"] = 700,
                ["__fmt"] = true
            }
        };

        var o = new O
        {
            ["tooltip"] = Tooltip("axis", sombra: true),
            ["grid"] = Grid(top: horizontal ? 8 : 24, right: horizontal ? 48 : 12),
            ["series"] = new object[] { serie }
        };
        AsignarEjes(o, etiquetas, horizontal);
        return o;
    }

    private static O BarrasAgrupadas(SerieShape s, WidgetConfig cfg, bool horizontal, bool apiladas)
    {
        var series = s.Series.Select((d, i) =>
        {
            var ultima = i == s.Series.Count - 1;
            var radio = horizontal
                ? (apiladas && !ultima ? new[] { 0, 0, 0, 0 } : new[] { 0, 6, 6, 0 })
                : (apiladas && !ultima ? new[] { 0, 0, 0, 0 } : new[] { 6, 6, 0, 0 });
            var serie = new O
            {
                ["type"] = "bar",
                ["name"] = d.Nombre,
                ["data"] = d.Valores.ToList(),
                ["barMaxWidth"] = apiladas ? 44 : 22,
                ["itemStyle"] = new O { ["color"] = Color(cfg.Tono, i), ["borderRadius"] = radio },
                ["emphasis"] = new O { ["focus"] = "series" }
            };
            if (apiladas)
            {
                serie["stack"] = "total";
            }

            return (object)serie;
        }).ToArray();

        var o = new O
        {
            ["tooltip"] = Tooltip("axis", sombra: true),
            ["legend"] = Leyenda(),
            ["grid"] = Grid(top: 34),
            ["series"] = series
        };
        AsignarEjes(o, s.Ejes.ToList(), horizontal);
        return o;
    }

    /// <summary>Pareto (una fuente): barras de mayor a menor + linea de % acumulado en el eje derecho.</summary>
    private static O Pareto(CategoriasShape c, WidgetConfig cfg)
    {
        var total = c.Total;
        decimal acum = 0;
        var porcentajes = c.Items.Select(i =>
        {
            acum += i.Valor;
            return total == 0 ? 0m : Math.Round(acum / total * 100, 1);
        }).ToList();
        var barras = Color(cfg.Tono, 0);
        var linea = Color(cfg.Tono, 4);

        return new O
        {
            ["__kind"] = "pareto",
            ["tooltip"] = Tooltip("axis", sombra: true),
            ["legend"] = Leyenda(),
            ["grid"] = Grid(top: 34, right: 8),
            ["xAxis"] = EjeCategoria(c.Items.Select(i => i.Etiqueta).ToList()),
            ["yAxis"] = new object[]
            {
                EjeValor(),
                new O
                {
                    ["type"] = "value", ["min"] = 0, ["max"] = 100, ["splitLine"] = new O { ["show"] = false },
                    ["axisLabel"] = new O { ["color"] = Tinta3, ["fontSize"] = 11, ["formatter"] = "{value} %" }
                }
            },
            ["series"] = new object[]
            {
                new O
                {
                    ["type"] = "bar", ["name"] = "Valor", ["data"] = c.Items.Select(i => i.Valor).ToList(), ["barMaxWidth"] = 40,
                    ["itemStyle"] = new O { ["color"] = Degradado(barras, 1, 0.5), ["borderRadius"] = new[] { 8, 8, 0, 0 } }
                },
                new O
                {
                    ["type"] = "line", ["name"] = "% acumulado", ["yAxisIndex"] = 1, ["data"] = porcentajes, ["smooth"] = true,
                    ["symbol"] = "circle", ["symbolSize"] = 6, ["lineStyle"] = new O { ["width"] = 2.5, ["color"] = linea },
                    ["itemStyle"] = new O { ["color"] = linea }
                }
            }
        };
    }

    /// <summary>Combinado: primera serie en barras (eje izquierdo), el resto en lineas (eje derecho).</summary>
    private static O BarrasYLinea(SerieShape s, WidgetConfig cfg)
    {
        var series = s.Series.Select((d, i) => (object)(i == 0
            ? new O
            {
                ["type"] = "bar",
                ["name"] = d.Nombre,
                ["data"] = d.Valores.ToList(),
                ["barMaxWidth"] = 40,
                ["itemStyle"] = new O { ["color"] = Degradado(Color(cfg.Tono, 0), 1, 0.5), ["borderRadius"] = new[] { 8, 8, 0, 0 } }
            }
            : new O
            {
                ["type"] = "line",
                ["name"] = d.Nombre,
                ["yAxisIndex"] = 1,
                ["data"] = d.Valores.ToList(),
                ["smooth"] = true,
                ["symbol"] = "circle",
                ["symbolSize"] = 6,
                ["connectNulls"] = true,
                ["lineStyle"] = new O { ["width"] = 2.5, ["color"] = Color(cfg.Tono, i + 3) },
                ["itemStyle"] = new O { ["color"] = Color(cfg.Tono, i + 3) }
            })).ToArray();

        return new O
        {
            ["tooltip"] = Tooltip("axis", sombra: true),
            ["legend"] = Leyenda(),
            ["grid"] = Grid(top: 34),
            ["xAxis"] = EjeCategoria(s.Ejes.ToList()),
            ["yAxis"] = new object[] { EjeValor(), EjeValor(sinRejilla: true) },
            ["series"] = series
        };
    }

    // ---------------------------------------------------------------- circulares y jerarquicos

    private static O Dona(CategoriasShape c, WidgetConfig cfg, Func<decimal?, string> formatear)
    {
        var datos = c.Items.Select((it, i) => new O
        {
            ["name"] = it.Etiqueta,
            ["value"] = it.Valor,
            ["itemStyle"] = new O { ["color"] = it.Tono is string t ? $"var(--t-{t})" : Color(cfg.Tono, i) }
        }).ToList();

        var titulo = new O
        {
            ["text"] = formatear(c.Total),
            ["subtext"] = "total",
            ["left"] = "29%",
            ["top"] = "center",
            ["textAlign"] = "center",
            ["itemGap"] = 2,
            ["textStyle"] = new O { ["color"] = Tinta, ["fontSize"] = 20, ["fontWeight"] = 800 },
            ["subtextStyle"] = new O { ["color"] = Tinta3, ["fontSize"] = 10, ["fontWeight"] = 600 }
        };

        var baseOption = new O
        {
            ["tooltip"] = Tooltip("item", sombra: false),
            ["title"] = titulo,
            ["legend"] = new O
            {
                ["type"] = "scroll",
                ["orient"] = "vertical",
                ["right"] = 0,
                ["top"] = "middle",
                ["width"] = "48%",
                ["icon"] = "circle",
                ["itemWidth"] = 9,
                ["itemHeight"] = 9,
                ["itemGap"] = 10,
                ["textStyle"] = new O { ["color"] = Tinta, ["fontSize"] = 12.5, ["fontWeight"] = 600 },
                ["pageTextStyle"] = new O { ["color"] = Tinta3 }
            },
            ["series"] = new object[]
            {
                new O
                {
                    ["type"] = "pie",
                    ["name"] = "Valor",
                    ["radius"] = new[] { "56%", "80%" },
                    ["center"] = new[] { "30%", "50%" },
                    ["avoidLabelOverlap"] = true,
                    ["padAngle"] = c.Items.Count > 1 ? 1.5 : 0,
                    ["itemStyle"] = new O { ["borderColor"] = Superficie, ["borderWidth"] = 2, ["borderRadius"] = 5 },
                    ["label"] = new O { ["show"] = false },
                    ["labelLine"] = new O { ["show"] = false },
                    ["emphasis"] = new O { ["scale"] = true, ["scaleSize"] = 6 },
                    ["data"] = datos
                }
            }
        };

        // Angosto (tarjeta de 1 columna en telefono): la leyenda pasa abajo y la dona al centro.
        return new O
        {
            ["__kind"] = "pie",
            ["baseOption"] = baseOption,
            ["media"] = new object[]
            {
                new O
                {
                    ["query"] = new O { ["maxWidth"] = 360 },
                    ["option"] = new O
                    {
                        ["title"] = new O { ["left"] = "50%", ["top"] = "34%" },
                        ["legend"] = new O { ["orient"] = "horizontal", ["right"] = "center", ["top"] = "bottom", ["width"] = "100%" },
                        ["series"] = new object[] { new O { ["center"] = new[] { "50%", "42%" }, ["radius"] = new[] { "44%", "64%" } } }
                    }
                }
            }
        };
    }

    private static O Embudo(CategoriasShape c, WidgetConfig cfg) => new()
    {
        ["__kind"] = "funnel",
        ["tooltip"] = Tooltip("item", sombra: false),
        ["series"] = new object[]
        {
            new O
            {
                ["type"] = "funnel",
                ["name"] = "Valor",
                ["left"] = "2%",
                ["top"] = 8,
                ["bottom"] = 8,
                ["width"] = "60%",
                ["minSize"] = "16%",
                ["sort"] = "descending",
                ["gap"] = 3,
                ["itemStyle"] = new O { ["borderColor"] = Superficie, ["borderWidth"] = 1 },
                ["label"] = new O { ["show"] = true, ["position"] = "right", ["color"] = Tinta, ["fontSize"] = 12, ["fontWeight"] = 600 },
                ["labelLine"] = new O { ["length"] = 12, ["lineStyle"] = new O { ["color"] = Linea2 } },
                ["emphasis"] = new O { ["label"] = new O { ["fontWeight"] = 800 } },
                ["data"] = c.Items.Select((it, i) => new O
                {
                    ["name"] = it.Etiqueta,
                    ["value"] = it.Valor,
                    ["itemStyle"] = new O { ["color"] = it.Tono is string t ? $"var(--t-{t})" : Color(cfg.Tono, i) }
                }).ToList()
            }
        }
    };

    private static O Treemap(CategoriasShape c, WidgetConfig cfg) => TreemapBase(
        c.Items.Select((it, i) => (object)new O
        {
            ["name"] = it.Etiqueta,
            ["value"] = it.Valor,
            ["itemStyle"] = new O { ["color"] = it.Tono is string t ? $"var(--t-{t})" : Color(cfg.Tono, i) }
        }).ToList(), dosNiveles: false);

    /// <summary>Dos niveles: el eje es el padre y cada serie con dato es un hijo.</summary>
    private static O Treemap(SerieShape s, WidgetConfig cfg) => TreemapBase(
        s.Ejes.Select((eje, e) => (object)new O
        {
            ["name"] = eje,
            ["itemStyle"] = new O { ["color"] = Color(cfg.Tono, e) },
            ["children"] = s.Series
                .Select(d => (d.Nombre, Valor: e < d.Valores.Count ? d.Valores[e] : null))
                .Where(x => x.Valor is > 0)
                .Select(x => new O { ["name"] = x.Nombre, ["value"] = x.Valor })
                .ToList()
        }).ToList(), dosNiveles: true);

    private static O TreemapBase(IReadOnlyList<object> datos, bool dosNiveles)
    {
        var serie = new O
        {
            ["type"] = "treemap",
            ["name"] = "Todo", // raiz de las migas de pan

            ["roam"] = false,
            ["nodeClick"] = dosNiveles ? "zoomToNode" : false,
            ["left"] = 0,
            ["right"] = 0,
            ["top"] = 0,
            ["bottom"] = dosNiveles ? 26 : 0,
            ["breadcrumb"] = new O
            {
                ["show"] = dosNiveles,
                ["bottom"] = 0,
                ["itemStyle"] = new O { ["color"] = Superficie3, ["borderColor"] = Linea2, ["textStyle"] = new O { ["color"] = Tinta2 } }
            },
            ["label"] = new O { ["show"] = true, ["color"] = "#fff", ["fontSize"] = 12, ["fontWeight"] = 700, ["overflow"] = "truncate" },
            ["upperLabel"] = new O { ["show"] = dosNiveles, ["height"] = 22, ["color"] = "#fff", ["fontWeight"] = 700 },
            ["itemStyle"] = new O { ["borderColor"] = Superficie, ["borderWidth"] = 2, ["gapWidth"] = 2, ["borderRadius"] = 6 },
            ["data"] = datos
        };

        if (dosNiveles)
        {
            // Nivel 0 = raiz virtual (sin etiqueta propia), 1 = padres (con etiqueta superior), 2 = hijos.
            serie["levels"] = new object[]
            {
                new O { ["upperLabel"] = new O { ["show"] = false }, ["itemStyle"] = new O { ["borderWidth"] = 0, ["gapWidth"] = 4 } },
                new O { ["upperLabel"] = new O { ["show"] = true }, ["itemStyle"] = new O { ["gapWidth"] = 2 } },
                new O { ["colorSaturation"] = new[] { 0.35, 0.6 }, ["itemStyle"] = new O { ["gapWidth"] = 1, ["borderColorSaturation"] = 0.7 } }
            };
        }

        return new O
        {
            ["__kind"] = "treemap",
            ["tooltip"] = Tooltip("item", sombra: false),
            ["series"] = new object[] { serie }
        };
    }

    // ---------------------------------------------------------------- series y mapas

    private static O Lineas(SerieShape s, WidgetConfig cfg, bool area)
    {
        var muchos = s.Ejes.Count > 24;
        var series = s.Series.Select((d, i) =>
        {
            var color = Color(cfg.Tono, i);
            var serie = new O
            {
                ["type"] = "line",
                ["name"] = d.Nombre,
                ["data"] = d.Valores.ToList(),
                ["smooth"] = true,
                ["smoothMonotone"] = "x", // la curva no rebota por encima/debajo de los datos
                ["symbol"] = "circle",
                ["symbolSize"] = 7,
                ["showSymbol"] = !muchos,
                ["lineStyle"] = new O { ["width"] = 2.5, ["color"] = color },
                ["itemStyle"] = new O { ["color"] = color, ["borderColor"] = Superficie, ["borderWidth"] = 2 },
                ["emphasis"] = new O { ["focus"] = "series" }
            };
            if (area)
            {
                serie["areaStyle"] = new O { ["color"] = Degradado(color, s.Series.Count > 1 ? 0.18 : 0.3, 0.02) };
            }

            return (object)serie;
        }).ToArray();

        var o = new O
        {
            ["tooltip"] = Tooltip("axis", sombra: false),
            ["grid"] = Grid(top: s.Series.Count > 1 ? 34 : 16),
            ["xAxis"] = EjeCategoria(s.Ejes.ToList(), sinHueco: true),
            ["yAxis"] = EjeValor(),
            ["series"] = series
        };

        if (s.Series.Count > 1)
        {
            o["legend"] = Leyenda();
        }

        if (muchos)
        {
            // Series largas: zoom con la rueda / pellizco dentro del grafico.
            o["dataZoom"] = new object[] { new O { ["type"] = "inside", ["start"] = 0, ["end"] = 100 } };
        }

        return o;
    }

    private static O MapaDeCalor(HeatmapShape h, WidgetConfig cfg)
    {
        var datos = new List<object?>();
        decimal max = 0;
        for (var r = 0; r < h.Filas.Count; r++)
        {
            for (var c = 0; c < h.Columnas.Count; c++)
            {
                if (h.Valores[r][c] is decimal v)
                {
                    datos.Add(new object[] { c, r, v });
                    max = Math.Max(max, v);
                }
            }
        }

        var color = Color(cfg.Tono, 0);
        return new O
        {
            ["tooltip"] = Tooltip("item", sombra: false),
            ["grid"] = Grid(top: 8, bottom: 4),
            ["xAxis"] = new O
            {
                ["type"] = "category",
                ["data"] = h.Columnas,
                ["position"] = "top",
                ["splitArea"] = new O { ["show"] = false },
                ["axisLine"] = new O { ["show"] = false },
                ["axisTick"] = new O { ["show"] = false },
                ["axisLabel"] = EtiquetaEje(rotar: h.Columnas.Count > 6)
            },
            ["yAxis"] = new O
            {
                ["type"] = "category",
                ["data"] = h.Filas,
                ["inverse"] = true,
                ["axisLine"] = new O { ["show"] = false },
                ["axisTick"] = new O { ["show"] = false },
                ["axisLabel"] = EtiquetaEje(rotar: false)
            },
            ["visualMap"] = new O
            {
                ["show"] = false,
                ["min"] = 0,
                ["max"] = max <= 0 ? 1 : max,
                ["dimension"] = 2,
                ["inRange"] = new O { ["color"] = new[] { $"{color}/0.1", color } }
            },
            ["series"] = new object[]
            {
                new O
                {
                    ["type"] = "heatmap",
                    ["name"] = "Valor",
                    ["data"] = datos,
                    ["label"] = new O
                    {
                        ["show"] = datos.Count <= 120, ["fontSize"] = 11, ["fontWeight"] = 700, ["__fmt"] = true,
                        ["color"] = Tinta, ["textBorderColor"] = Superficie, ["textBorderWidth"] = 2
                    },
                    ["itemStyle"] = new O { ["borderColor"] = Superficie, ["borderWidth"] = 3, ["borderRadius"] = 6 },
                    ["emphasis"] = new O { ["itemStyle"] = new O { ["shadowBlur"] = 8, ["shadowColor"] = "rgba(0,0,0,.25)" } }
                }
            }
        };
    }

    private static O Calendario(CalendarioShape cal, WidgetConfig cfg)
    {
        var color = Color(cfg.Tono, 0);
        var max = cal.Dias.Count == 0 ? 1 : Math.Max(1, cal.Dias.Max(d => d.Valor));
        return new O
        {
            ["__kind"] = "calendar",
            ["tooltip"] = Tooltip("item", sombra: false),
            ["visualMap"] = new O
            {
                ["show"] = false,
                ["min"] = 0,
                ["max"] = max,
                ["inRange"] = new O { ["color"] = new[] { $"{color}/0.22", color } }
            },
            ["calendar"] = new O
            {
                ["range"] = new[] { Iso(cal.Desde), Iso(cal.Hasta) },
                ["top"] = 24,
                ["left"] = 28,
                ["right"] = 6,
                ["bottom"] = 4,
                ["cellSize"] = new object[] { "auto", 14 },
                ["splitLine"] = new O { ["show"] = false },
                ["itemStyle"] = new O { ["color"] = Superficie3, ["borderColor"] = Superficie, ["borderWidth"] = 3 },
                ["yearLabel"] = new O { ["show"] = false },
                ["dayLabel"] = new O { ["firstDay"] = 1, ["nameMap"] = new[] { "D", "L", "M", "M", "J", "V", "S" }, ["color"] = Tinta3, ["fontSize"] = 10 },
                ["monthLabel"] = new O { ["nameMap"] = MesesCortos, ["color"] = Tinta3, ["fontSize"] = 10, ["fontWeight"] = 600 }
            },
            ["series"] = new object[]
            {
                new O
                {
                    ["type"] = "heatmap",
                    ["name"] = "Valor",
                    ["coordinateSystem"] = "calendar",
                    ["data"] = cal.Dias.Select(d => new object[] { Iso(d.Dia), d.Valor }).ToList()
                }
            }
        };
    }

    private static O Velocimetro(EscalarShape k, WidgetConfig cfg, Func<decimal?, string> formatear)
    {
        var valor = k.Valor ?? 0m;
        var tope = cfg.Meta ?? cfg.UmbralAlto;
        var max = tope is decimal m && m > 0 ? m : EscalaRedonda(valor);
        var color = k.Tono is string t ? $"var(--t-{t})" : Color(cfg.Tono, 0);

        // Fondo: bandas suaves de los umbrales (si hay) o la pista neutra.
        object fondo = new object[] { new object[] { 1, Superficie3 } };
        if (cfg.UmbralBajo is decimal bajo && cfg.UmbralAlto is decimal alto && alto > bajo && max > 0)
        {
            var (malo, bueno) = cfg.MayorEsMejor ? ("rose", "green") : ("green", "rose");
            fondo = new object[]
            {
                new object[] { Fraccion(bajo, max), $"var(--t-{malo})/0.22" },
                new object[] { Fraccion(alto, max), "var(--t-amber)/0.22" },
                new object[] { 1, $"var(--t-{bueno})/0.22" }
            };
        }

        var nombre = tope is decimal ? $"Meta {formatear(max)}" : $"Escala {formatear(max)}";
        return new O
        {
            ["tooltip"] = new O { ["show"] = false },
            ["series"] = new object[]
            {
                new O
                {
                    ["type"] = "gauge",
                    ["startAngle"] = 210,
                    ["endAngle"] = -30,
                    ["min"] = 0,
                    ["max"] = max,
                    ["radius"] = "94%",
                    ["center"] = new[] { "50%", "60%" },
                    ["progress"] = new O { ["show"] = true, ["width"] = 14, ["roundCap"] = true, ["itemStyle"] = new O { ["color"] = color } },
                    ["axisLine"] = new O { ["roundCap"] = true, ["lineStyle"] = new O { ["width"] = 14, ["color"] = fondo } },
                    ["pointer"] = new O { ["show"] = false },
                    ["anchor"] = new O { ["show"] = false },
                    ["axisTick"] = new O { ["show"] = false },
                    ["splitLine"] = new O { ["show"] = false },
                    ["axisLabel"] = new O { ["show"] = false },
                    ["title"] = new O { ["show"] = true, ["offsetCenter"] = new object[] { 0, "38%" }, ["fontSize"] = 12, ["color"] = Tinta3, ["fontWeight"] = 600 },
                    ["detail"] = new O
                    {
                        ["valueAnimation"] = true, ["offsetCenter"] = new object[] { 0, "-2%" }, ["fontSize"] = 26, ["fontWeight"] = 800,
                        ["color"] = Tinta, ["__fmt"] = true
                    },
                    ["data"] = new object[] { new O { ["value"] = Math.Min(valor, max * 1.0m), ["name"] = nombre, ["__real"] = valor } }
                }
            }
        };
    }

    // ---------------------------------------------------------------- piezas comunes

    private static O Tooltip(string trigger, bool sombra)
    {
        var t = new O
        {
            ["trigger"] = trigger,
            ["confine"] = true,
            ["backgroundColor"] = Superficie,
            ["borderColor"] = Linea2,
            ["borderWidth"] = 1,
            ["padding"] = new[] { 8, 12 },
            ["textStyle"] = new O { ["color"] = Tinta, ["fontSize"] = 12 },
            ["extraCssText"] = "border-radius:10px;box-shadow:0 8px 24px rgba(0,0,0,.14);"
        };
        if (trigger == "axis")
        {
            t["axisPointer"] = sombra
                ? new O { ["type"] = "shadow", ["shadowStyle"] = new O { ["color"] = "var(--ink)/0.05" } }
                : new O { ["type"] = "line", ["lineStyle"] = new O { ["color"] = Linea2, ["type"] = "dashed" } };
        }

        return t;
    }

    private static O Leyenda() => new()
    {
        ["type"] = "scroll",
        ["top"] = 0,
        ["left"] = 0,
        ["icon"] = "circle",
        ["itemWidth"] = 9,
        ["itemHeight"] = 9,
        ["itemGap"] = 14,
        ["textStyle"] = new O { ["color"] = Tinta2, ["fontSize"] = 12, ["fontWeight"] = 600 },
        ["pageTextStyle"] = new O { ["color"] = Tinta3 }
    };

    private static O Grid(int top = 24, int right = 12, int bottom = 4) => new()
    {
        ["left"] = 4,
        ["right"] = right,
        ["top"] = top,
        ["bottom"] = bottom,
        ["containLabel"] = true
    };

    private static void AsignarEjes(O o, IReadOnlyList<string> etiquetas, bool horizontal)
    {
        if (horizontal)
        {
            var cat = EjeCategoria(etiquetas);
            cat["inverse"] = true;
            ((O)cat["axisLabel"]!)["width"] = 110;
            o["yAxis"] = cat;
            o["xAxis"] = EjeValor();
        }
        else
        {
            o["xAxis"] = EjeCategoria(etiquetas);
            o["yAxis"] = EjeValor();
        }
    }

    private static O EjeCategoria(IReadOnlyList<string> datos, bool sinHueco = false) => new()
    {
        ["type"] = "category",
        ["data"] = datos,
        ["boundaryGap"] = !sinHueco,
        ["axisLine"] = new O { ["lineStyle"] = new O { ["color"] = Linea2 } },
        ["axisTick"] = new O { ["show"] = false },
        ["axisLabel"] = EtiquetaEje(rotar: false)
    };

    private static O EtiquetaEje(bool rotar) => new()
    {
        ["color"] = Tinta3,
        ["fontSize"] = 11,
        ["fontWeight"] = 600,
        ["hideOverlap"] = true,
        ["width"] = 90,
        ["overflow"] = "truncate",
        ["rotate"] = rotar ? 35 : 0
    };

    private static O EjeValor(bool sinRejilla = false) => new()
    {
        ["type"] = "value",
        ["splitLine"] = new O { ["show"] = !sinRejilla, ["lineStyle"] = new O { ["color"] = Linea2, ["type"] = "dashed" } },
        ["axisLabel"] = new O { ["color"] = Tinta3, ["fontSize"] = 11, ["__compacto"] = true }
    };

    /// <summary>Degradado vertical (arriba -> abajo) del mismo color con dos opacidades.</summary>
    private static O Degradado(string color, double alfaArriba, double alfaAbajo) => new()
    {
        ["type"] = "linear",
        ["x"] = 0,
        ["y"] = 0,
        ["x2"] = 0,
        ["y2"] = 1,
        ["colorStops"] = new object[]
        {
            new O { ["offset"] = 0, ["color"] = ConAlfa(color, alfaArriba) },
            new O { ["offset"] = 1, ["color"] = ConAlfa(color, alfaAbajo) }
        }
    };

    /// <summary>Degradado horizontal (izquierda -> derecha).</summary>
    private static O DegradadoH(string color, double alfaIzq, double alfaDer) => new()
    {
        ["type"] = "linear",
        ["x"] = 0,
        ["y"] = 0,
        ["x2"] = 1,
        ["y2"] = 0,
        ["colorStops"] = new object[]
        {
            new O { ["offset"] = 0, ["color"] = ConAlfa(color, alfaIzq) },
            new O { ["offset"] = 1, ["color"] = ConAlfa(color, alfaDer) }
        }
    };

    private static string ConAlfa(string color, double alfa) =>
        alfa >= 1 ? color : $"{color}/{alfa.ToString("0.##", CultureInfo.InvariantCulture)}";

    private static double Fraccion(decimal v, decimal max) => Math.Clamp((double)(v / max), 0, 1);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Tope "redondo" de la escala para un valor (1, 2, 2.5, 5 x 10^n), con holgura del 20%.</summary>
    public static decimal EscalaRedonda(decimal valor)
    {
        var objetivo = Math.Max(1m, Math.Abs(valor) * 1.2m);
        var potencia = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)objetivo)));
        foreach (var paso in new[] { 1m, 2m, 2.5m, 5m, 10m })
        {
            if (paso * potencia >= objetivo)
            {
                return paso * potencia;
            }
        }

        return 10m * potencia;
    }
}
