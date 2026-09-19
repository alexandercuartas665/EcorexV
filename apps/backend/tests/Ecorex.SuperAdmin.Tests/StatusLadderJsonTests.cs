using System.Text.Json;
using Ecorex.SuperAdmin.Components.Shared.Forms;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Editor del escalon de estados (FormBuilder OLA 3): arma / lee form_definitions.status_ladder_json que consume
/// el motor FormStatusLadder.Resolve. Modelo: {"field","states":[{"label","when":[{field,op,value|min}]}]}.
/// </summary>
public class StatusLadderJsonTests
{
    private static StatusLadderJson.Cond C(string f, string op, string? v = null, int? min = null) => new(f, op, v, min);

    [Fact]
    public void Build_arma_field_y_estados_en_orden()
    {
        var states = new List<StatusLadderJson.State>
        {
            new("Inicial", new()),
            new("Perfilado", new() { C("bant_1", "equals", "true"), C("bant_2", "equals", "true") }),
            new("Prospectado", new() { C("oportunidad", "hasChildren") }),
            new("Cerrado", new() { C("oportunidad", "childCount", min: 2), C("cerrado", "equals", "true") }),
        };
        var json = StatusLadderJson.Build("estado_lead", states);
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;
        Assert.Equal("estado_lead", root.GetProperty("field").GetString());
        var st = root.GetProperty("states").EnumerateArray().ToList();
        Assert.Equal(4, st.Count);
        Assert.Equal("Inicial", st[0].GetProperty("label").GetString());
        Assert.Empty(st[0].GetProperty("when").EnumerateArray());   // piso sin condiciones

        var perf = st[1].GetProperty("when").EnumerateArray().ToList();
        Assert.Equal(2, perf.Count);
        Assert.Equal("bant_1", perf[0].GetProperty("field").GetString());
        Assert.Equal("equals", perf[0].GetProperty("op").GetString());
        Assert.Equal("true", perf[0].GetProperty("value").GetString());

        // hasChildren no escribe value ni min.
        var pros = st[2].GetProperty("when")[0];
        Assert.Equal("hasChildren", pros.GetProperty("op").GetString());
        Assert.False(pros.TryGetProperty("value", out _));
        Assert.False(pros.TryGetProperty("min", out _));

        // childCount escribe min (no value).
        var cerr = st[3].GetProperty("when")[0];
        Assert.Equal("childCount", cerr.GetProperty("op").GetString());
        Assert.Equal(2, cerr.GetProperty("min").GetInt32());
        Assert.False(cerr.TryGetProperty("value", out _));
    }

    [Fact]
    public void Build_devuelve_null_sin_campo_destino()
        => Assert.Null(StatusLadderJson.Build("", new List<StatusLadderJson.State> { new("X", new()) }));

    [Fact]
    public void Build_devuelve_null_si_no_hay_estados_con_etiqueta()
        => Assert.Null(StatusLadderJson.Build("estado", new List<StatusLadderJson.State> { new("  ", new()) }));

    [Fact]
    public void Build_omite_condiciones_sin_campo()
    {
        var json = StatusLadderJson.Build("estado", new List<StatusLadderJson.State>
        {
            new("Uno", new() { C("", "equals", "x"), C("real", "equals", "y") }),
        });
        using var doc = JsonDocument.Parse(json!);
        var when = doc.RootElement.GetProperty("states")[0].GetProperty("when").EnumerateArray().ToList();
        Assert.Single(when);
        Assert.Equal("real", when[0].GetProperty("field").GetString());
    }

    [Fact]
    public void Build_childCount_sin_min_usa_1()
    {
        var json = StatusLadderJson.Build("estado", new List<StatusLadderJson.State>
        {
            new("Uno", new() { C("sub", "childCount", min: null) }),
        });
        using var doc = JsonDocument.Parse(json!);
        Assert.Equal(1, doc.RootElement.GetProperty("states")[0].GetProperty("when")[0].GetProperty("min").GetInt32());
    }

    [Fact]
    public void Read_roundtrip_de_lo_que_arma_Build()
    {
        var states = new List<StatusLadderJson.State>
        {
            new("Inicial", new()),
            new("Perfilado", new() { C("bant_1", "equals", "true") }),
            new("Cerrado", new() { C("op", "childCount", min: 3) }),
        };
        var json = StatusLadderJson.Build("estado_lead", states);
        var read = StatusLadderJson.Read(json);
        Assert.NotNull(read);
        Assert.Equal("estado_lead", read!.Field);
        Assert.Equal(3, read.States.Count);
        Assert.Equal("Perfilado", read.States[1].Label);
        Assert.Equal("bant_1", read.States[1].When[0].Field);
        Assert.Equal("true", read.States[1].When[0].Value);
        Assert.Equal(3, read.States[2].When[0].Min);
        Assert.Equal("childCount", read.States[2].When[0].Op);
    }

    [Fact]
    public void Read_null_si_json_vacio_invalido_o_sin_field()
    {
        Assert.Null(StatusLadderJson.Read(null));
        Assert.Null(StatusLadderJson.Read("   "));
        Assert.Null(StatusLadderJson.Read("no es json"));
        Assert.Null(StatusLadderJson.Read("""{"states":[{"label":"X"}]}"""));   // sin field
    }
}
