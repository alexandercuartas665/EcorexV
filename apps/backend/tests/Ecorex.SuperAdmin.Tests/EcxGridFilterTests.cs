using Ecorex.SuperAdmin.Components.Shared.Grid;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>Filtro Y/O y conversiones de la grilla comun (ADR-0207).</summary>
public class EcxGridFilterTests
{
    private sealed record Fila(string Nombre, string? Ciudad, decimal? Cupo, DateTime? Ingreso, bool Activo);

    private static readonly Dictionary<string, GridFilterField> Campos = new()
    {
        ["nombre"] = new("nombre", "Nombre", GridColType.Text, null),
        ["ciudad"] = new("ciudad", "Ciudad", GridColType.Text, null),
        ["cupo"] = new("cupo", "Cupo", GridColType.Number, "Ficha: Cliente"),
        ["ingreso"] = new("ingreso", "Ingreso", GridColType.Date, null),
        ["activo"] = new("activo", "Activo", GridColType.Bool, null),
    };

    private static GridFilterField? FieldOf(string k) => Campos.TryGetValue(k, out var f) ? f : null;

    private static object? Raw(Fila r, string k) => k switch
    {
        "nombre" => r.Nombre,
        "ciudad" => r.Ciudad,
        "cupo" => r.Cupo,
        "ingreso" => r.Ingreso,
        "activo" => r.Activo,
        _ => null,
    };

    private static bool Eval(GridFilterNode root, Fila r)
        => GridFilter.Evaluate(root, FieldOf, k => Raw(r, k), k => GridFormat.Format(Raw(r, k), FieldOf(k)!.Type));

    private static GridFilterNode Cond(string field, string op, string? v = null, string? v2 = null)
        => new() { Kind = "cond", Field = field, Operator = op, Value = v, Value2 = v2 };

    private static readonly Fila Cali = new("Acme SAS", "Santiago de Cali", 1500000m, new DateTime(2026, 3, 1), true);
    private static readonly Fila Bogota = new("Beta Ltda", "Bogota", null, null, false);

    [Fact]
    public void Y_exige_todas_y_O_alguna()
    {
        var y = GridFilterNode.NewGroup("and");
        y.Children.Add(Cond("ciudad", "contains", "cali"));
        y.Children.Add(Cond("activo", "istrue"));
        Assert.True(Eval(y, Cali));
        Assert.False(Eval(y, Bogota));

        var o = GridFilterNode.NewGroup("or");
        o.Children.Add(Cond("ciudad", "eq", "bogota"));
        o.Children.Add(Cond("cupo", "gt", "1000000"));
        Assert.True(Eval(o, Cali));
        Assert.True(Eval(o, Bogota));
    }

    [Fact]
    public void Condicion_incompleta_no_filtra_ni_cuenta()
    {
        var root = GridFilterNode.NewGroup("and");
        root.Children.Add(Cond("ciudad", "contains"));
        root.Children.Add(Cond("", ""));
        Assert.Equal(0, GridFilter.CountConditions(root, FieldOf));
        Assert.True(Eval(root, Bogota));
    }

    [Fact]
    public void Vacio_numero_fecha_y_lista()
    {
        Assert.True(Eval(Wrap(Cond("cupo", "empty")), Bogota));
        Assert.False(Eval(Wrap(Cond("cupo", "empty")), Cali));
        Assert.True(Eval(Wrap(Cond("cupo", "between", "1.000.000", "2000000")), Cali));
        Assert.True(Eval(Wrap(Cond("ingreso", "after", "2026-02-28")), Cali));
        Assert.False(Eval(Wrap(Cond("ingreso", "after", "2026-02-28")), Bogota));
        Assert.True(Eval(Wrap(Cond("ciudad", "in", "Medellin, Bogota")), Bogota));
        Assert.False(Eval(Wrap(Cond("ciudad", "in", "Medellin, Bogota")), Cali));
    }

    [Fact]
    public void Grupos_anidados_se_evaluan()
    {
        var root = GridFilterNode.NewGroup("and");
        root.Children.Add(Cond("activo", "isfalse"));
        var sub = GridFilterNode.NewGroup("or");
        sub.Children.Add(Cond("nombre", "contains", "beta"));
        sub.Children.Add(Cond("nombre", "contains", "gamma"));
        root.Children.Add(sub);
        Assert.Equal(3, GridFilter.CountConditions(root, FieldOf));
        Assert.True(Eval(root, Bogota));
        Assert.False(Eval(root, Cali));
    }

    [Theory]
    [InlineData("1500000", 1500000)]
    [InlineData("1.500.000", 1500000)]
    [InlineData("$ 1.500.000", 1500000)]
    [InlineData("12.5", 12.5)]
    public void ParseDecimal_acepta_formatos_comunes(string s, double esperado)
        => Assert.Equal((decimal)esperado, GridFormat.ParseDecimal(s));

    [Fact]
    public void Formato_de_fecha_bool_y_numero()
    {
        Assert.Equal("01/03/2026", GridFormat.Format(new DateTime(2026, 3, 1), GridColType.Date));
        Assert.Equal("Si", GridFormat.Format(true, GridColType.Bool));
        Assert.Equal("1.500.000", GridFormat.Format(1500000m, GridColType.Number));
        Assert.Equal("", GridFormat.Format(null, GridColType.Text));
    }

    private static GridFilterNode Wrap(GridFilterNode c)
    {
        var g = GridFilterNode.NewGroup("and");
        g.Children.Add(c);
        return g;
    }
}
