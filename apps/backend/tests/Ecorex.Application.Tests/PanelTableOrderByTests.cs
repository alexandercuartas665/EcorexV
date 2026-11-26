using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Panels;

namespace Ecorex.Application.Tests;

/// <summary>
/// Widget table con OrderBy/OrderDir: ordena por cualquier columna (directa o agregada) comparando por tipo
/// (fecha / numero / texto, vacios al final) y SIEMPRE antes de TopN. Sin OrderBy (o con uno que no coincide)
/// se conserva el orden de siempre: primera columna agregada desc.
/// </summary>
public class PanelTableOrderByTests
{
    private static PanelRow Row(params (string Key, object? Value)[] cells)
    {
        var r = new PanelRow();
        foreach (var (k, v) in cells)
        {
            r[k] = v;
        }

        return r;
    }

    private static DateTimeOffset D(int y, int m, int d) => new(y, m, d, 10, 0, 0, TimeSpan.Zero);

    // Directorio: un contacto por nombre, con fecha de actualizacion (Beto sin fecha).
    private static List<PanelRow> Contactos() => new()
    {
        Row(("Nombre", "Ana"), ("Ciudad", "Cali"), ("Actualizado", D(2026, 11, 20)), ("Puntos", 15m)),
        Row(("Nombre", "Beto"), ("Ciudad", "Bogota"), ("Actualizado", null), ("Puntos", 2m)),
        Row(("Nombre", "Carla"), ("Ciudad", "armenia"), ("Actualizado", D(2026, 11, 25)), ("Puntos", 100m)),
        Row(("Nombre", "Dario"), ("Ciudad", "Medellin"), ("Actualizado", D(2026, 10, 1)), ("Puntos", null)),
    };

    private static List<PanelColumn> Cols(string? fechaFormat = "datetime") => new()
    {
        new() { Label = "Contacto", Field = "Nombre" },
        new() { Label = "Ciudad", Field = "Ciudad" },
        new() { Label = "Ultima actualizacion", Field = "Actualizado", Format = fechaFormat },
        new() { Label = "Puntos", Field = "Puntos" },
    };

    private static List<string?> Names(List<List<object?>> rows) => rows.Select(r => r[0] as string).ToList();

    [Fact]
    public void OrderBy_Fecha_Desc_MasRecientePrimero_VaciosAlFinal()
    {
        var rows = PanelDataEngine.TableRows(Contactos(), "Nombre", Cols(), null, "Actualizado", "desc");
        Assert.Equal(new[] { "Carla", "Ana", "Dario", "Beto" }, Names(rows));
    }

    [Fact]
    public void OrderBy_Fecha_Asc_MasAntiguaPrimero_VaciosSiguenAlFinal()
    {
        // Por Label (gana sobre Field) y sin Format: el tipo fecha se infiere de los datos.
        var rows = PanelDataEngine.TableRows(Contactos(), "Nombre", Cols(fechaFormat: null), null, "ultima actualizacion", "asc");
        Assert.Equal(new[] { "Dario", "Ana", "Carla", "Beto" }, Names(rows));
    }

    [Fact]
    public void OrderBy_SinDireccion_EsDesc()
    {
        var rows = PanelDataEngine.TableRows(Contactos(), "Nombre", Cols(), null, "Actualizado", null);
        Assert.Equal("Carla", rows[0][0]);
    }

    [Fact]
    public void OrderBy_Numero_ComparaComoNumero_NoComoTexto()
    {
        // Como texto "100" < "15" < "2"; como numero 2 < 15 < 100.
        var rows = PanelDataEngine.TableRows(Contactos(), "Nombre", Cols(), null, "Puntos", "asc");
        Assert.Equal(new[] { "Beto", "Ana", "Carla", "Dario" }, Names(rows));
    }

    [Fact]
    public void OrderBy_Texto_SinDistinguirMayusculas()
    {
        var rows = PanelDataEngine.TableRows(Contactos(), "Nombre", Cols(), null, "Ciudad", "asc");
        Assert.Equal(new[] { "Carla", "Beto", "Ana", "Dario" }, Names(rows));
    }

    [Fact]
    public void OrderBy_ColumnaAgregada_Asc()
    {
        var cols = new List<PanelColumn>
        {
            new() { Label = "Ciudad", Field = "Ciudad" },
            new() { Label = "Contactos", Agg = "count" },
        };
        var data = Contactos();
        data.Add(Row(("Nombre", "Eva"), ("Ciudad", "Cali")));
        var rows = PanelDataEngine.TableRows(data, "Ciudad", cols, null, "Contactos", "asc");
        Assert.Equal(1m, rows[0][1]);
        Assert.Equal("Cali", rows[^1][0]);
    }

    [Fact]
    public void OrderBy_SeAplicaAntesDeTopN()
    {
        // TopN 2 debe quedarse con las 2 MAS recientes, no con las 2 primeras del agrupado.
        var rows = PanelDataEngine.TableRows(Contactos(), "Nombre", Cols(), 2, "Actualizado", "desc");
        Assert.Equal(new[] { "Carla", "Ana" }, Names(rows));
    }

    [Fact]
    public void OrderBy_Inexistente_CaeAlOrdenDeSiempre()
    {
        var cols = new List<PanelColumn>
        {
            new() { Label = "Ciudad", Field = "Ciudad" },
            new() { Label = "Puntos", Agg = "sum", AggField = "Puntos" },
        };
        var conOrden = PanelDataEngine.TableRows(Contactos(), "Ciudad", cols, null, "NoExiste", "asc");
        var sinOrden = PanelDataEngine.TableRows(Contactos(), "Ciudad", cols, null);
        Assert.Equal(sinOrden.Select(r => r[0]), conOrden.Select(r => r[0]));
        Assert.Equal(100m, conOrden[0][1]);
    }

    private static IReadOnlyList<ReportSourceDescriptor> Catalog() => new[]
    {
        new ReportSourceDescriptor("native:tercero", "Directorio", ReportSourceKind.Container, new[]
        {
            new ReportField("Nombre", "Nombre", ReportFieldType.Text, CanFilter: true, CanGroup: true, CanAggregate: false),
            new ReportField("Actualizado", "Actualizado", ReportFieldType.Date, CanFilter: true, CanGroup: true, CanAggregate: false),
        })
    };

    private static PanelSpec Spec(string orderBy, string? orderDir) => PanelSpec.FromJson(@"{
      ""title"": ""Directorio"",
      ""sources"": { ""main"": { ""container"": ""Directorio"" } },
      ""widgets"": [
        { ""type"": ""table"", ""groupBy"": ""Nombre"", ""topN"": 300, ""orderBy"": """ + orderBy + @""", ""orderDir"": " + (orderDir is null ? "null" : "\"" + orderDir + "\"") + @",
          ""columns"": [ { ""label"": ""Contacto"", ""field"": ""Nombre"" },
                         { ""label"": ""Ultima actualizacion"", ""field"": ""Actualizado"", ""format"": ""datetime"" } ] }
      ]
    }")!;

    [Fact]
    public void Validator_AceptaOrderByPorFieldOLabel_YDireccionValida()
    {
        Assert.Empty(PanelSpecValidator.Validate(Spec("Actualizado", "desc"), Catalog()));
        Assert.Empty(PanelSpecValidator.Validate(Spec("Ultima actualizacion", "ASC"), Catalog()));
        Assert.Empty(PanelSpecValidator.Validate(Spec("Actualizado", null), Catalog()));
    }

    [Fact]
    public void Validator_ReportaOrderBySinColumna_YDireccionInvalida()
    {
        Assert.Contains(PanelSpecValidator.Validate(Spec("Telefono", "desc"), Catalog()), e => e.Contains("Telefono"));
        Assert.Contains(PanelSpecValidator.Validate(Spec("Actualizado", "arriba"), Catalog()), e => e.Contains("arriba"));
    }
}
