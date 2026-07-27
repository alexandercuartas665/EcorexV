using Ecorex.Application.Forms;
using Ecorex.Application.Forms.Calc;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// Round-trip Excel de la tabla (export/plantilla/import): verifica que las columnas calculadas se
// excluyan de la plantilla y de la importacion, y que exportar+importar conserve las capturables.
public class FormGridXlsxSmokeTests
{
    private static List<FormGridColumn> Cols() => new()
    {
        new FormGridColumn("detalle", "Detalle", null, FormAggregate.None, null, "text"),
        new FormGridColumn("cantidad", "Cantidad", null, FormAggregate.None, null, "text"),
        new FormGridColumn("tipo", "Tipo", null, FormAggregate.None, null, "select",
            new List<FormOption> { new("hr", "HR"), new("inox", "INOX") }),
        new FormGridColumn("total", "Total", "{cantidad}*2", FormAggregate.Sum, null, "text"),
    };

    [Fact]
    public void Template_excluye_calculadas()
    {
        var bytes = FormGridXlsx.Template(Cols(), "Items");
        Assert.NotEmpty(bytes);
        // Reimportar la plantilla como si fuera datos: no tiene filas.
        var rows = FormGridXlsx.Import(bytes, Cols());
        Assert.Empty(rows);
    }

    [Fact]
    public void Export_luego_Import_conserva_capturables_y_omite_calculada()
    {
        var data = new List<IReadOnlyDictionary<string, string?>>
        {
            new Dictionary<string, string?> { ["detalle"] = "Lamina A", ["cantidad"] = "3", ["tipo"] = "HR", ["total"] = "6" },
            new Dictionary<string, string?> { ["detalle"] = "Lamina B", ["cantidad"] = "5", ["tipo"] = "INOX", ["total"] = "10" },
        };
        var xlsx = FormGridXlsx.Export(Cols(), data, "Items");
        var back = FormGridXlsx.Import(xlsx, Cols());

        Assert.Equal(2, back.Count);
        Assert.Equal("Lamina A", back[0]["detalle"]);
        Assert.Equal("3", back[0]["cantidad"]);
        Assert.DoesNotContain("total", back[0].Keys); // la calculada no se importa
        Assert.Equal("INOX", back[1]["tipo"]);
    }
}
