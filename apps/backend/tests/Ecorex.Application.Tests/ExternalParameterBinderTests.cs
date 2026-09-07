using Ecorex.Application.Reporting.External;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Enlace de parametros del conector externo (ADR-0064). Fija la seguridad de ALCANCE y de
/// PARAMETRIZACION:
/// - Los parametros de alcance (Context) toman su valor del contexto de confianza, NUNCA de entrada libre.
/// - Los parametros Input se convierten al tipo declarado y viajan como VALOR de parametro (no se
///   concatenan): un intento de inyeccion queda como el string literal de un parametro tipado.
/// Pieza pura, corre sin BD.
/// </summary>
public class ExternalParameterBinderTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid User = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void ContextParameter_ResolvesFromTrustedContext_IgnoringInputs()
    {
        var declared = new[]
        {
            new ExternalDataSetParameter("userid", ExternalDataParameterType.Guid, ExternalDataParameterBinding.Context, ContextKey: "userid"),
            new ExternalDataSetParameter("sucursal", ExternalDataParameterType.String, ExternalDataParameterBinding.Context, ContextKey: "sucursal")
        };
        var ctx = new ExternalRunContext(Tenant, User, new Dictionary<string, string?> { ["sucursal"] = "BOG" });

        // Un atacante intenta sobreescribir el alcance por "entrada": debe ser IGNORADO.
        var inputs = new Dictionary<string, string?> { ["userid"] = Guid.NewGuid().ToString(), ["sucursal"] = "TODAS" };

        var bound = ExternalParameterBinder.Bind(declared, ctx, inputs).ToDictionary(p => p.Name);

        Assert.Equal(User, bound["userid"].Value);       // del contexto, no del input
        Assert.Equal("BOG", bound["sucursal"].Value);    // del contexto (Extra), no del input
    }

    [Fact]
    public void ContextTenantId_ResolvesFromContext()
    {
        var declared = new[]
        {
            new ExternalDataSetParameter("tenantid", ExternalDataParameterType.Guid, ExternalDataParameterBinding.Context, ContextKey: "tenantid")
        };
        var bound = ExternalParameterBinder.Bind(declared, new ExternalRunContext(Tenant), null);
        Assert.Equal(Tenant, Assert.Single(bound).Value);
    }

    [Fact]
    public void InputParameter_ConvertsToDeclaredType_AndUsesDefaultWhenMissing()
    {
        var declared = new[]
        {
            new ExternalDataSetParameter("id_encargado", ExternalDataParameterType.Int, ExternalDataParameterBinding.Input),
            new ExternalDataSetParameter("fecha_ini", ExternalDataParameterType.Date, ExternalDataParameterBinding.Input, DefaultValue: "2026-01-01")
        };
        var inputs = new Dictionary<string, string?> { ["id_encargado"] = "42" };

        var bound = ExternalParameterBinder.Bind(declared, new ExternalRunContext(Tenant), inputs).ToDictionary(p => p.Name);

        Assert.Equal(42L, bound["id_encargado"].Value);                       // convertido a entero
        Assert.Equal(new DateTime(2026, 1, 1), bound["fecha_ini"].Value);     // default aplicado y convertido
    }

    [Fact]
    public void InjectionAttempt_StaysAsTypedParameterValue_NotConcatenated()
    {
        // El valor NO se concatena en SQL: viaja como el valor de un parametro string. Aqui se ve que el
        // binder lo entrega literal (el executor lo enlaza como DbParameter, jamas como texto del comando).
        var declared = new[] { new ExternalDataSetParameter("nombre", ExternalDataParameterType.String, ExternalDataParameterBinding.Input) };
        var payload = "1; DROP TABLE tareas; --";
        var inputs = new Dictionary<string, string?> { ["nombre"] = payload };

        var bound = Assert.Single(ExternalParameterBinder.Bind(declared, new ExternalRunContext(Tenant), inputs));
        Assert.Equal(payload, bound.Value);
    }

    [Fact]
    public void MalformedTypedInput_BecomesNull_NotRawText()
    {
        // Un valor no convertible a entero NO se cuela como texto: queda NULL tipado.
        var declared = new[] { new ExternalDataSetParameter("id", ExternalDataParameterType.Int, ExternalDataParameterBinding.Input) };
        var inputs = new Dictionary<string, string?> { ["id"] = "42 OR 1=1" };

        var bound = Assert.Single(ExternalParameterBinder.Bind(declared, new ExternalRunContext(Tenant), inputs));
        Assert.Null(bound.Value);
    }

    [Fact]
    public void UnresolvedContextKey_BecomesNull()
    {
        var declared = new[] { new ExternalDataSetParameter("sede", ExternalDataParameterType.String, ExternalDataParameterBinding.Context, ContextKey: "sede") };
        var bound = Assert.Single(ExternalParameterBinder.Bind(declared, new ExternalRunContext(Tenant), null));
        Assert.Null(bound.Value);
    }

    // ---- RowLimit (ADR-0068/0084): el default de autoria NO capa el reporte ----

    [Fact]
    public void RowLimit_InReportContext_BindsToMaxRows_IgnoringAuthorDefault()
    {
        // @limite con DefaultValue=5 (pensado para probar en el editor). En un REPORTE debe traer hasta el
        // tope del sistema, no 5.
        var declared = new[]
        {
            new ExternalDataSetParameter("limite", ExternalDataParameterType.Int, ExternalDataParameterBinding.RowLimit, DefaultValue: "5")
        };
        var bound = Assert.Single(ExternalParameterBinder.Bind(declared, new ExternalRunContext(Tenant), inputs: null, reportRowLimit: 50_000));
        Assert.Equal(50_000L, bound.Value);
    }

    [Fact]
    public void RowLimit_InConsoleContext_UsesTypedInputThenDefault()
    {
        var declared = new[]
        {
            new ExternalDataSetParameter("limite", ExternalDataParameterType.Int, ExternalDataParameterBinding.RowLimit, DefaultValue: "5")
        };

        // Consola con valor tecleado: usa ese valor (no el tope de reporte).
        var typed = Assert.Single(ExternalParameterBinder.Bind(declared, new ExternalRunContext(Tenant),
            new Dictionary<string, string?> { ["limite"] = "20" }));
        Assert.Equal(20L, typed.Value);

        // Consola sin valor: usa el DefaultValue de autoria.
        var def = Assert.Single(ExternalParameterBinder.Bind(declared, new ExternalRunContext(Tenant), inputs: null));
        Assert.Equal(5L, def.Value);
    }

    // ---- MULTI-VALOR (SSRS `IN (@p)`): 1..N valores tipados por parametro ----

    [Fact]
    public void MultiValueInput_SplitsIntoTypedValues_ByLineAndComma()
    {
        // Un parametro multi-valor entero: la entrada trae varios codigos (por linea y por coma). Cada uno se
        // convierte al tipo declarado y viaja en Values; Value queda null (lo expande el executor).
        var declared = new[]
        {
            new ExternalDataSetParameter("grupo", ExternalDataParameterType.Int, ExternalDataParameterBinding.Input, MultiValue: true)
        };
        var inputs = new Dictionary<string, string?> { ["grupo"] = "1\n2,3\r\n4" };

        var bound = Assert.Single(ExternalParameterBinder.Bind(declared, new ExternalRunContext(Tenant), inputs));
        Assert.Null(bound.Value);
        Assert.NotNull(bound.Values);
        Assert.Equal(new object?[] { 1L, 2L, 3L, 4L }, bound.Values!);
    }

    [Fact]
    public void MultiValueInput_EmptyText_ProducesEmptyValueList()
    {
        // Sin valores: lista vacia (el executor lo traduce a IN (NULL) => ninguna fila, sin error de sintaxis).
        var declared = new[]
        {
            new ExternalDataSetParameter("grupo", ExternalDataParameterType.String, ExternalDataParameterBinding.Input, MultiValue: true)
        };
        var bound = Assert.Single(ExternalParameterBinder.Bind(declared, new ExternalRunContext(Tenant),
            new Dictionary<string, string?> { ["grupo"] = "   " }));
        Assert.NotNull(bound.Values);
        Assert.Empty(bound.Values!);
    }

    [Fact]
    public void MultiValueInput_InjectionPerValue_StaysAsTypedValue()
    {
        // Cada valor de una lista multi-valor tambien viaja como VALOR (no se concatena).
        var declared = new[]
        {
            new ExternalDataSetParameter("codigo", ExternalDataParameterType.String, ExternalDataParameterBinding.Input, MultiValue: true)
        };
        var payload = "01'); DROP TABLE ventas; --";
        var bound = Assert.Single(ExternalParameterBinder.Bind(declared, new ExternalRunContext(Tenant),
            new Dictionary<string, string?> { ["codigo"] = $"01\n{payload}" }));
        Assert.Equal(new object?[] { "01", payload }, bound.Values!);
    }
}
