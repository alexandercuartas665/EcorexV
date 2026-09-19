using Ecorex.Application.Forms.Lookups;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Proyeccion de un dataset externo (SQL) a items de lookup del formulario (FormBuilder Ola 6/A2). Modelo de
/// copia: Value == Display; los campos pedidos se copian por nombre de columna.
/// </summary>
public class ExternalDatasetProjectionTests
{
    private static readonly string[] Cols = { "NOMBRE", "REFERENCIA", "PRECIO" };
    private static IReadOnlyList<IReadOnlyList<string?>> Rows => new IReadOnlyList<string?>[]
    {
        new string?[] { "Lamina cold rolled", "LCR-01", "381466.40" },
        new string?[] { "Lamina galvanizada", "LGV-02", "420000" },
        new string?[] { "Tornillo hex", "TX-9", "1200" },
    };

    [Fact]
    public void Project_usa_displayfield_y_copia_campos()
    {
        var items = ExternalDatasetProjection.Project(Cols, Rows, "NOMBRE", new[] { "REFERENCIA", "PRECIO" });
        Assert.Equal(3, items.Count);
        Assert.Equal("Lamina cold rolled", items[0].Display);
        Assert.Equal("Lamina cold rolled", items[0].Value);          // modelo de copia: value == display
        Assert.Equal("LCR-01", items[0].Fields["REFERENCIA"]);
        Assert.Equal("381466.40", items[0].Fields["PRECIO"]);
    }

    [Fact]
    public void Project_sin_displayfield_toma_la_primera_columna_con_valor()
    {
        var rows = new IReadOnlyList<string?>[] { new string?[] { "", "REF-1", "9" } };
        var items = ExternalDatasetProjection.Project(Cols, rows, displayField: null, fields: System.Array.Empty<string>());
        Assert.Equal("REF-1", items[0].Display);   // NOMBRE vacio -> primera con valor
    }

    [Fact]
    public void Project_columna_desconocida_da_null()
    {
        var items = ExternalDatasetProjection.Project(Cols, Rows, "NOMBRE", new[] { "NO_EXISTE" });
        Assert.Null(items[0].Fields["NO_EXISTE"]);
    }

    [Fact]
    public void Filter_por_etiqueta_o_por_campo_copiado()
    {
        var items = ExternalDatasetProjection.Project(Cols, Rows, "NOMBRE", new[] { "REFERENCIA" });
        // por etiqueta
        var lam = ExternalDatasetProjection.Filter(items, "lamina");
        Assert.Equal(2, lam.Count);
        // por campo copiado (referencia)
        var tx = ExternalDatasetProjection.Filter(items, "TX-9");
        Assert.Single(tx);
        Assert.Equal("Tornillo hex", tx[0].Display);
    }

    [Fact]
    public void Filter_query_vacio_devuelve_todo()
    {
        var items = ExternalDatasetProjection.Project(Cols, Rows, "NOMBRE", System.Array.Empty<string>());
        Assert.Equal(3, ExternalDatasetProjection.Filter(items, "  ").Count);
        Assert.Equal(3, ExternalDatasetProjection.Filter(items, null).Count);
    }
}
