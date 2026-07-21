using Ecorex.Application.DataLookups;

namespace Ecorex.Application.Tests;

/// <summary>
/// Mecanica de CASCADA y AUTOLLENADO de los campos tipo lista. Son pruebas unitarias a proposito:
/// el resolutor no toca base de datos, y la logica que interesa (que pasa cuando el campo padre
/// esta vacio, que se limpia al cambiar la seleccion) se puede fijar sin levantar contenedores.
/// </summary>
public class DataLookupBinderTests
{
    private static readonly Guid Tabla = Guid.CreateVersion7();
    private static readonly Guid ColGrupo = Guid.CreateVersion7();
    private static readonly Guid ColMarca = Guid.CreateVersion7();

    [Fact]
    public void Dependencias_Son_Los_Campos_Origen_Sin_Repetir()
    {
        var cfg = Config(
            new DataLookupFilterConfig(ColGrupo, FromFieldKey: "grupo"),
            new DataLookupFilterConfig(ColMarca, FromFieldKey: "GRUPO"),
            new DataLookupFilterConfig(ColMarca, Value: "fijo"));

        // Sin duplicar y sin distinguir mayusculas; el filtro de valor fijo no es dependencia.
        Assert.Equal(new[] { "grupo" }, DataLookupBinder.DependenciesOf(cfg));
        Assert.Empty(DataLookupBinder.DependenciesOf(Config()));
    }

    [Fact]
    public void Filtro_Toma_El_Valor_Del_Campo_Padre()
    {
        var cfg = Config(new DataLookupFilterConfig(ColGrupo, FromFieldKey: "grupo"));

        var r = DataLookupBinder.ResolveFilters(cfg, new Dictionary<string, string?> { ["grupo"] = "Herramienta" });

        Assert.False(r.Blocked);
        Assert.Equal("Herramienta", r.Filters[ColGrupo]);
        Assert.Empty(r.MissingSources);
    }

    [Fact]
    public void Padre_Vacio_Sin_Respaldo_Omite_El_Filtro_Pero_No_Bloquea()
    {
        var cfg = Config(new DataLookupFilterConfig(ColGrupo, FromFieldKey: "grupo"));

        // Por defecto la lista se ve completa mientras no se elija el padre.
        foreach (var estado in new[]
        {
            new Dictionary<string, string?>(),
            new Dictionary<string, string?> { ["grupo"] = null },
            new Dictionary<string, string?> { ["grupo"] = "   " }
        })
        {
            var r = DataLookupBinder.ResolveFilters(cfg, estado);
            Assert.False(r.Blocked);
            Assert.Empty(r.Filters);
            Assert.Equal(new[] { "grupo" }, r.MissingSources);
        }

        // Y sin estado alguno tampoco revienta.
        Assert.Empty(DataLookupBinder.ResolveFilters(cfg, null).Filters);
    }

    [Fact]
    public void Con_RequireSource_El_Padre_Vacio_Bloquea_La_Lista()
    {
        var cfg = Config(new DataLookupFilterConfig(ColGrupo, FromFieldKey: "grupo", RequireSource: true));

        var vacio = DataLookupBinder.ResolveFilters(cfg, new Dictionary<string, string?>());
        Assert.True(vacio.Blocked);
        Assert.Equal(new[] { "grupo" }, vacio.MissingSources);

        // Con el padre lleno deja de bloquear.
        var lleno = DataLookupBinder.ResolveFilters(cfg, new Dictionary<string, string?> { ["grupo"] = "Material" });
        Assert.False(lleno.Blocked);
        Assert.Equal("Material", lleno.Filters[ColGrupo]);
    }

    [Fact]
    public void Value_Actua_De_Respaldo_Cuando_El_Padre_Esta_Vacio()
    {
        var cfg = Config(new DataLookupFilterConfig(ColGrupo, Value: "Herramienta", FromFieldKey: "grupo"));

        var sinPadre = DataLookupBinder.ResolveFilters(cfg, new Dictionary<string, string?>());
        Assert.Equal("Herramienta", sinPadre.Filters[ColGrupo]);
        Assert.False(sinPadre.Blocked);

        // Con el padre lleno, el padre MANDA sobre el respaldo.
        var conPadre = DataLookupBinder.ResolveFilters(cfg, new Dictionary<string, string?> { ["grupo"] = "Material" });
        Assert.Equal("Material", conPadre.Filters[ColGrupo]);
    }

    [Fact]
    public void Filtro_Fijo_Se_Aplica_Y_El_Filtro_Vacio_Se_Ignora()
    {
        var cfg = Config(
            new DataLookupFilterConfig(ColGrupo, Value: "Herramienta"),
            new DataLookupFilterConfig(ColMarca));

        var r = DataLookupBinder.ResolveFilters(cfg, null);
        Assert.Single(r.Filters);
        Assert.Equal("Herramienta", r.Filters[ColGrupo]);
    }

    [Fact]
    public void Dos_Filtros_Sobre_La_Misma_Columna_Conservan_El_Primero()
    {
        // No se pueden cumplir a la vez; que gane el ultimo en silencio seria peor.
        var cfg = Config(
            new DataLookupFilterConfig(ColGrupo, Value: "Herramienta"),
            new DataLookupFilterConfig(ColGrupo, Value: "Material"));

        var r = DataLookupBinder.ResolveFilters(cfg, null);
        Assert.Equal("Herramienta", Assert.Single(r.Filters).Value);
    }

    [Fact]
    public void Autollenado_Vuelca_Los_Valores_Y_Limpia_Los_Que_Ya_No_Estan()
    {
        var cfg = Config() with
        {
            Autofill = new[]
            {
                new DataLookupAutofillConfig(ColGrupo, "grupo"),
                new DataLookupAutofillConfig(ColMarca, "marca")
            }
        };

        var fila = new DataLookupRowDto(Guid.CreateVersion7(), "Martillo",
            new Dictionary<Guid, string?> { [ColGrupo] = "Herramienta", [ColMarca] = "Stanley" });

        var vals = DataLookupBinder.BuildAutofill(cfg, fila);
        Assert.Equal("Herramienta", vals["grupo"]);
        Assert.Equal("Stanley", vals["marca"]);

        // Fila sin una de las columnas: el destino se LIMPIA en vez de conservar lo anterior,
        // que ya no corresponde a lo elegido.
        var parcial = new DataLookupRowDto(Guid.CreateVersion7(), "Cemento",
            new Dictionary<Guid, string?> { [ColGrupo] = "Material" });
        var vals2 = DataLookupBinder.BuildAutofill(cfg, parcial);
        Assert.Equal("Material", vals2["grupo"]);
        Assert.Null(vals2["marca"]);

        // Al deseleccionar (fila null) se limpian todos los destinos.
        var vacio = DataLookupBinder.BuildAutofill(cfg, null);
        Assert.Null(vacio["grupo"]);
        Assert.Null(vacio["marca"]);
    }

    [Fact]
    public void ColumnsNeeded_Junta_Autollenado_Y_Filtros()
    {
        // Sin autollenado se trae la fila completa (null = sin recorte).
        Assert.Null(DataLookupBinder.ColumnsNeeded(Config()));

        var cfg = Config(new DataLookupFilterConfig(ColGrupo, FromFieldKey: "grupo")) with
        {
            Autofill = new[] { new DataLookupAutofillConfig(ColMarca, "marca") }
        };

        // La columna filtrada tambien se trae: permite revalidar la seleccion sin otra consulta.
        var cols = DataLookupBinder.ColumnsNeeded(cfg)!;
        Assert.Contains(ColMarca, cols);
        Assert.Contains(ColGrupo, cols);
        Assert.Equal(2, cols.Count);
    }

    [Fact]
    public void StillMatches_Detecta_Cuando_La_Seleccion_Dejo_De_Ser_Valida()
    {
        var fila = new DataLookupRowDto(Guid.CreateVersion7(), "Martillo",
            new Dictionary<Guid, string?> { [ColGrupo] = "Herramienta", [ColMarca] = "Stanley" });

        // Sin filtros, cualquier fila vale.
        Assert.True(DataLookupBinder.StillMatches(fila, new Dictionary<Guid, string>()));

        // Coincide (sin distinguir mayusculas, igual que la consulta).
        Assert.True(DataLookupBinder.StillMatches(fila, new Dictionary<Guid, string> { [ColGrupo] = "herramienta" }));

        // El padre cambio a otro grupo: la seleccion ya no vale y hay que limpiarla.
        Assert.False(DataLookupBinder.StillMatches(fila, new Dictionary<Guid, string> { [ColGrupo] = "Material" }));

        // Si la fila ni siquiera trae la columna filtrada, no se puede afirmar que cumple.
        Assert.False(DataLookupBinder.StillMatches(fila, new Dictionary<Guid, string> { [Guid.CreateVersion7()] = "x" }));
    }

    private static DataLookupConfig Config(params DataLookupFilterConfig[] filtros)
        => new(Tabla, Filters: filtros.Length == 0 ? null : filtros);
}
