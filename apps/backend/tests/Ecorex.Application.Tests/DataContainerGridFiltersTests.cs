using Ecorex.Application.Forms.Lookups;

namespace Ecorex.Application.Tests;

/// <summary>
/// Filtro de una columna de grilla sobre un Contenedor (cascada Familia -> Articulo): el renderer ya
/// sustituyo los tokens {columna}; el adaptador toma los pares col=valor y un valor vacio no filtra.
/// </summary>
public class DataContainerGridFiltersTests
{
    [Fact]
    public void Plano_con_valor_filtra()
    {
        var f = DataContainerLookupSource.GridFilters("{\"Familia\":\" Tornilleria \"}");
        Assert.NotNull(f);
        Assert.Equal("Tornilleria", f!["familia"]);
    }

    [Fact]
    public void Objeto_fixed_tambien_sirve()
        => Assert.Equal("X", DataContainerLookupSource.GridFilters("{\"fixed\":{\"Familia\":\"X\"}}")!["Familia"]);

    [Fact]
    public void Valor_vacio_no_filtra_y_ofrece_todo()
    {
        Assert.Null(DataContainerLookupSource.GridFilters("{\"Familia\":\"\"}"));
        Assert.Null(DataContainerLookupSource.GridFilters("{\"Familia\":\"   \"}"));
    }

    [Fact]
    public void Sin_filtro_o_invalido_es_null()
    {
        Assert.Null(DataContainerLookupSource.GridFilters(null));
        Assert.Null(DataContainerLookupSource.GridFilters("no json"));
        Assert.Null(DataContainerLookupSource.GridFilters("[1,2]"));
        Assert.Null(DataContainerLookupSource.GridFilters("{\"cascade\":[{\"a\":1}]}"));
    }
}
