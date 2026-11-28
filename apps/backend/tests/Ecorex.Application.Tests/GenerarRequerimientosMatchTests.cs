using Ecorex.Application.Forms.Lookups;
using Ecorex.Application.Rules.Verbs;
using V = Ecorex.Application.Rules.Verbs.GenerarRequerimientosDesdeTablaVerb;

namespace Ecorex.Application.Tests;

/// <summary>
/// Emparejamiento del verbo GENERAR_REQUERIMIENTOS_DESDE_TABLA: el origen por fila (id o nombre) y el
/// solicitante (id, correo o nombre visible del campo "Usuarios del sistema"). Puro, sin base de datos.
/// </summary>
public class GenerarRequerimientosMatchTests
{
    private static readonly Guid Ofi = Guid.NewGuid();
    private static readonly Guid Pla = Guid.NewGuid();
    private static readonly Guid Viejo = Guid.NewGuid();

    private static readonly List<V.OrigenRef> Origenes =
    [
        new(Viejo, "Planta produccion", "PLANTA", false),
        new(Ofi, "Insumos de Oficina", "OFICINA", true),
        new(Pla, "Planta produccion", "PLANTA", true),
    ];

    [Fact]
    public void MatchOrigen_por_id()
        => Assert.Equal(Ofi, V.MatchOrigen(Ofi.ToString(), Origenes)?.Id);

    [Fact]
    public void MatchOrigen_por_nombre_sin_mayusculas_y_prefiere_activo()
        => Assert.Equal(Pla, V.MatchOrigen("  planta   PRODUCCION ", Origenes)?.Id);

    [Fact]
    public void MatchOrigen_por_area_si_no_hay_nombre()
        => Assert.Equal(Ofi, V.MatchOrigen("oficina", Origenes)?.Id);

    [Fact]
    public void MatchOrigen_sin_coincidencia_o_vacio_es_null()
    {
        Assert.Null(V.MatchOrigen("Bodega", Origenes));
        Assert.Null(V.MatchOrigen("", Origenes));
        Assert.Null(V.MatchOrigen(Guid.NewGuid().ToString(), Origenes));
        Assert.Null(V.MatchOrigen("Planta produccion", null));
    }

    private static readonly Guid Richard = Guid.NewGuid();
    private static readonly Guid Diego = Guid.NewGuid();
    private static readonly Guid SinNombre = Guid.NewGuid();

    private static readonly List<V.UserRef> Users =
    [
        new(Richard, "Richard Gonzales Perdomo", "direccionventas@agrometalicas.com"),
        new(Diego, "Diego Fernando Rojas", "proyectos@agrometalicas.com"),
        new(SinNombre, null, "ana.garcia@agrometalicas.com"),
    ];

    [Fact]
    public void MatchUser_por_nombre_visible()
        => Assert.Equal(Richard, V.MatchUser("richard  gonzales perdomo", Users));

    [Fact]
    public void MatchUser_por_correo_e_id()
    {
        Assert.Equal(Diego, V.MatchUser("PROYECTOS@agrometalicas.com", Users));
        Assert.Equal(Diego, V.MatchUser(Diego.ToString(), Users));
    }

    [Fact]
    public void MatchUser_por_nombre_derivado_del_correo()
        => Assert.Equal(SinNombre, V.MatchUser("Ana Garcia", Users));

    [Fact]
    public void MatchUser_ambiguo_o_desconocido_es_null()
    {
        var dup = new List<V.UserRef>(Users) { new(Guid.NewGuid(), "Diego Fernando Rojas", "otro@x.com") };
        Assert.Null(V.MatchUser("Diego Fernando Rojas", dup));
        Assert.Null(V.MatchUser("Pedro Perez", Users));
        Assert.Null(V.MatchUser(Guid.NewGuid().ToString(), Users));
    }

    [Fact]
    public void UserLabel_igual_al_del_campo_usuarios()
    {
        Assert.Equal("Ana Garcia", SystemUserLookupSource.UserLabel(null, "ana.garcia@x.com"));
        Assert.Equal("Richard", SystemUserLookupSource.UserLabel(" Richard ", "r@x.com"));
    }

    [Fact]
    public void Descriptor_ofrece_solicitante_en_el_mapeo()
    {
        var mapeo = new V(null!, null!, null!).Descriptor.Params.Single(p => p.Name == "mapeo");
        Assert.Contains(mapeo.Options!, o => o.Value == "solicitante");
        Assert.Contains(mapeo.Options!, o => o.Value == "origen");
    }
}
