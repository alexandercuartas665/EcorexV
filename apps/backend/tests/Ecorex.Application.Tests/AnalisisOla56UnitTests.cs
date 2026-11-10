using Ecorex.Application.Analisis;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// ADR-0158 Olas 5-6 y pendientes (puro, sin BD): widget combinado (llaves, cruce, formulas, formas),
/// limites de fecha en la zona del tenant, filtro de tablero por Id, Categoria del Directorio y hash del
/// enlace publico.
/// </summary>
public class AnalisisOla56UnitTests
{
    private static readonly ReportSourceDescriptor Tareas = new TaskItemReportSource(null!).Describe();
    private static readonly ReportSourceDescriptor Directorio = new TerceroReportSource(null!).Describe();
    private static readonly ReportSourceDescriptor Contenedor = new("container:abc", "Ventas", ReportSourceKind.Container, new[]
    {
        new ReportField("c1", "Fecha", ReportFieldType.Date),
        new ReportField("c2", "Cliente", ReportFieldType.Text)
    });

    private static ReportDataSet Ds(params (object? Llave, decimal? Valor)[] filas) =>
        new(new[] { new ReportColumn("k", "k", ReportFieldType.Text), new ReportColumn("Count", "Count", ReportFieldType.Number) },
            filas.Select(f => (IReadOnlyList<object?>)new[] { f.Llave, (object?)f.Valor }).ToList());

    private static CombinadoDiseno Diseno(LlaveCombinado llave = LlaveCombinado.Texto, bool comunes = false, params string[] formulas) => new()
    {
        Series = new[]
        {
            new SerieCombinada { Nombre = "Clientes", SourceKey = TerceroReportSource.SourceKey, Dimension = "Ciudad" },
            new SerieCombinada { Nombre = "Tareas", SourceKey = TaskItemReportSource.SourceKey, Dimension = "Cliente" }
        },
        Formulas = formulas.Select((f, i) => new FormulaCombinada($"F{i}", f)).ToList(),
        Llave = llave,
        SoloComunes = comunes
    };

    [Theory]
    [InlineData(" Bogota ", "BOGOTA")]
    [InlineData("Medellin", "MEDELLIN")]
    [InlineData("Medell\u00edn", "MEDELLIN")]
    [InlineData("san   andres", "SAN ANDRES")]
    public void Llave_Texto_TrimSinTildesMayusculas(string valor, string esperado) =>
        Assert.Equal(esperado, CombinadoDiseno.Normalizar(valor, LlaveCombinado.Texto));

    [Fact]
    public void Llave_Nit_QuitaDv_Y_Exacta_TalCual()
    {
        Assert.Equal(CombinadoDiseno.Normalizar("900123456", LlaveCombinado.Nit), CombinadoDiseno.Normalizar("900.123.456-7", LlaveCombinado.Nit));
        Assert.Equal(" Bogota", CombinadoDiseno.Normalizar(" Bogota", LlaveCombinado.Exacta));
        Assert.Null(CombinadoDiseno.Normalizar("  ", LlaveCombinado.Texto));
        Assert.Null(CombinadoDiseno.Normalizar(null, LlaveCombinado.Nit));
    }

    [Fact]
    public void Combinar_UnionAlineaYSumaDuplicadosNormalizados()
    {
        var t = CombinadoMotor.Combinar(Diseno(), new[]
        {
            Ds(("Bogota", 3), ("bogota ", 2), ("Cali", 1)),
            Ds(("BOGOTA", 10), ("Medellin", 4))
        });

        Assert.Equal(new[] { "Clientes", "Tareas" }, t.Columnas);
        var bog = Assert.Single(t.Filas, f => f.Etiqueta == "Bogota");
        Assert.Equal(new decimal?[] { 5, 10 }, bog.Valores);
        Assert.Equal(new decimal?[] { 1, null }, t.Filas.Single(f => f.Etiqueta == "Cali").Valores);
        Assert.Equal(new decimal?[] { null, 4 }, t.Filas.Single(f => f.Etiqueta == "Medellin").Valores);
        Assert.Empty(t.Avisos);
    }

    [Fact]
    public void Combinar_SoloComunes_Interseccion_Y_AvisoSiNoHayCruce()
    {
        var t = CombinadoMotor.Combinar(Diseno(comunes: true), new[] { Ds(("A", 1), ("B", 2)), Ds(("b", 5)) });
        Assert.Equal("B", Assert.Single(t.Filas).Etiqueta);

        var vacio = CombinadoMotor.Combinar(Diseno(comunes: true), new[] { Ds(("A", 1)), Ds(("Z", 5)) });
        Assert.Empty(vacio.Filas);
        Assert.Single(vacio.Avisos);
    }

    [Fact]
    public void Combinar_Formulas_FilaPorFila_DivisionPorCeroEsNull()
    {
        var t = CombinadoMotor.Combinar(Diseno(LlaveCombinado.Texto, false, "B / A * 100"), new[]
        {
            Ds(("X", 4), ("Y", 0)),
            Ds(("X", 1), ("Y", 3))
        });

        Assert.Equal(3, t.Columnas.Count);
        Assert.Equal(25m, t.Filas.Single(f => f.Etiqueta == "X").Valores[2]);
        Assert.Null(t.Filas.Single(f => f.Etiqueta == "Y").Valores[2]);
    }

    [Fact]
    public void Combinar_TopeDeLlavesPorSerie_RecortaYAvisa()
    {
        var muchas = Enumerable.Range(0, CombinadoDiseno.MaxLlavesPorSerie + 10).Select(i => ((object?)$"k{i}", (decimal?)1)).ToArray();
        var t = CombinadoMotor.Combinar(Diseno(), new[] { Ds(muchas), Ds() });
        Assert.Equal(CombinadoDiseno.MaxLlavesPorSerie, t.Filas.Count);
        Assert.Contains(t.Avisos, a => a.StartsWith("Serie A", StringComparison.Ordinal));
    }

    [Fact]
    public void AForma_Kpi_FormulaSobreTotales_NoPromedioDeTasas()
    {
        var d = Diseno(LlaveCombinado.Texto, false, "B / A * 100");
        var t = CombinadoMotor.Combinar(d, new[] { Ds(("X", 10), ("Y", 30)), Ds(("X", 5), ("Y", 5)) });
        var kpi = Assert.IsType<EscalarShape>(CombinadoMotor.AForma(t, d, AnalisisTipoWidget.Kpi, 8, "Conversion"));
        Assert.Equal(25m, kpi.Valor); // 10 / 40 * 100, no (50% + 16.7%) / 2
    }

    [Fact]
    public void AForma_BarrasAgrupadas_Tabla_Y_Dona()
    {
        var d = Diseno(LlaveCombinado.Texto, false, "A + B");
        var t = CombinadoMotor.Combinar(d, new[] { Ds(("X", 1), ("Y", 9)), Ds(("X", 2)) });

        var barras = Assert.IsType<SerieShape>(CombinadoMotor.AForma(t, d, AnalisisTipoWidget.Barras, 8, "t"));
        Assert.Equal(new[] { "Y", "X" }, barras.Ejes); // por la serie A de mayor a menor
        Assert.Equal(3, barras.Series.Count);

        var tabla = Assert.IsType<TablaShape>(CombinadoMotor.AForma(t, d, AnalisisTipoWidget.Tabla, 8, "t"));
        Assert.Equal(4, tabla.Columnas.Count);

        var dona = Assert.IsType<CategoriasShape>(CombinadoMotor.AForma(t, d, AnalisisTipoWidget.Dona, 8, "t"));
        Assert.Equal(3m, dona.Items.Single(i => i.Etiqueta == "X").Valor); // usa la formula (ultima columna)
    }

    [Theory]
    [InlineData("A + B", 2, true)]
    [InlineData("(A - B) / 2", 2, true)]
    [InlineData("-A * 1.5", 1, true)]
    [InlineData("C / A", 2, false)] // C no existe con 2 series
    [InlineData("A +", 2, false)]
    [InlineData("A; drop", 2, false)]
    [InlineData("", 2, false)]
    public void Formula_Validacion(string expr, int series, bool valida) =>
        Assert.Equal(valida, FormulaSerie.EsValida(expr, series));

    [Fact]
    public void Validar_ExigeFuenteCruceYCampoNumerico()
    {
        var fuentes = new[] { Tareas, Directorio };
        Assert.Empty(Diseno().Validar(fuentes));

        var malo = new CombinadoDiseno
        {
            Series = new[]
            {
                new SerieCombinada { SourceKey = "nope", Dimension = "x" },
                new SerieCombinada { SourceKey = TaskItemReportSource.SourceKey, Dimension = "Title", Funcion = ReportAggregateFunction.Sum, CampoMedida = "Title" }
            }
        };
        var errores = malo.Validar(fuentes);
        Assert.Contains(errores, e => e.StartsWith("Serie A", StringComparison.Ordinal));
        Assert.Contains(errores, e => e.Contains("numerico", StringComparison.Ordinal));
    }

    [Fact]
    public void Config_CombinadoViajaEnElConfigJson()
    {
        var json = new WidgetConfig { Combinado = Diseno(LlaveCombinado.Nit, true, "A / B") }.ToJson();
        var leido = WidgetConfig.Leer(json).Combinado;
        Assert.NotNull(leido);
        Assert.Equal(LlaveCombinado.Nit, leido!.Llave);
        Assert.True(leido.SoloComunes);
        Assert.Equal(2, leido.Series.Count);
        Assert.Equal("A / B", leido.Formulas[0].Expresion);
    }

    [Fact]
    public void Spec_DeSerie_EsAgregadoConFiltrosExtra()
    {
        var spec = CombinadoDiseno.SpecDe(
            new SerieCombinada { SourceKey = "s", Dimension = "d", Funcion = ReportAggregateFunction.Sum, CampoMedida = "m", Filtros = new[] { new WidgetFiltroDiseno("f", ReportFilterOperator.In, "a, b") } },
            new[] { new ReportFilter("g", ReportFilterOperator.Equals, new[] { "x" }) });
        Assert.Equal(new[] { "d" }, spec.GroupBy);
        Assert.Equal(new ReportAggregate("m", ReportAggregateFunction.Sum), Assert.Single(spec.Aggregates));
        Assert.Equal(new[] { "a", "b" }, spec.Filters[0].Values);
        Assert.Equal("g", spec.Filters[1].FieldKey);
    }

    // ---- Fechas en la zona del tenant (regla 9) ----

    [Fact]
    public void Limite_FuenteNativa_UsaOffsetDelTenant_ContenedorDiaCalendario()
    {
        var bogota = TimeZoneInfo.CreateCustomTimeZone("co", TimeSpan.FromHours(-5), "co", "co");
        var dia = new DateOnly(2026, 11, 1);
        Assert.Equal("2026-11-01T00:00:00-05:00", HojaFiltroBinder.Limite(dia, Tareas, bogota));
        Assert.Equal("2026-11-01", HojaFiltroBinder.Limite(dia, Contenedor, bogota));
        Assert.Equal("2026-11-01", HojaFiltroBinder.Limite(dia, Tareas, null));
    }

    [Fact]
    public void FiltrosHoja_ConZona_HastaIncluyeElDiaCompletoLocal()
    {
        var bogota = TimeZoneInfo.CreateCustomTimeZone("co", TimeSpan.FromHours(-5), "co", "co");
        var f = HojaFiltroBinder.Filtros(new HojaFiltros { Desde = new DateOnly(2026, 11, 1), Hasta = new DateOnly(2026, 11, 30) }, Tareas, null, bogota);
        Assert.Equal("2026-11-01T00:00:00-05:00", f.Single(x => x.Operator == ReportFilterOperator.GreaterThanOrEqual).Values[0]);
        Assert.Equal("2026-12-01T00:00:00-05:00", f.Single(x => x.Operator == ReportFilterOperator.LessThan).Values[0]);
    }

    [Fact]
    public void MesClave_UsaLaZonaDelTenant_NoUtc()
    {
        // 1 de diciembre 02:00 UTC = 30 de noviembre 21:00 en Bogota (-5): el mes del usuario es noviembre.
        var bogota = TimeZoneInfo.CreateCustomTimeZone("co", TimeSpan.FromHours(-5), "co", "co");
        var instante = new DateTimeOffset(2026, 12, 1, 2, 0, 0, TimeSpan.Zero);
        Assert.Equal("2026-11", Ecorex.Application.Reporting.Sources.InMemoryReportEngine.MonthKey(instante, bogota));
        Assert.Equal("2026-12", Ecorex.Application.Reporting.Sources.InMemoryReportEngine.MonthKey(instante, null));
        Assert.Null(Ecorex.Application.Reporting.Sources.InMemoryReportEngine.MonthKey(null, bogota));
    }

    // ---- Pendientes de fuentes ----

    [Fact]
    public void Tareas_ExponeTableroPorId_SoloFiltrable()
    {
        var f = Tareas.FindField("BoardId");
        Assert.NotNull(f);
        Assert.True(f!.CanFilter);
        Assert.False(f.CanGroup);
    }

    [Fact]
    public void Directorio_ExponeCategoria()
    {
        var f = Directorio.FindField("Categoria");
        Assert.NotNull(f);
        Assert.True(f!.CanGroup);
    }

    [Fact]
    public void EnlacePublico_HashEsSha256Hex()
    {
        var h = AnalisisCompartirService.Hash("abc");
        Assert.Equal(64, h.Length);
        Assert.Equal(h, AnalisisCompartirService.Hash("abc"));
        Assert.NotEqual(h, AnalisisCompartirService.Hash("abd"));
    }
}
