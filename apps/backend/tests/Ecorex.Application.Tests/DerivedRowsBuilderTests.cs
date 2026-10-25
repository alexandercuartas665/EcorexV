using Ecorex.Application.DataContainers;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Motor puro de tablas derivadas (ADR-0127): normalizacion de claves por tipo, tipos de los agregados,
/// division por cero en Avg, filtro previo, proyeccion distinta sin agregados y validacion de la spec.
/// La poda de grupos huerfanos y la regla "un solo nivel" se prueban en integracion (dependen de la BD),
/// pero aqui se fija el contrato del que dependen: la clave es determinista y estable.
/// </summary>
public class DerivedRowsBuilderTests
{
    private static readonly Guid Ciudad = Guid.NewGuid();
    private static readonly Guid Monto = Guid.NewGuid();
    private static readonly Guid Fecha = Guid.NewGuid();
    private static readonly Guid Activo = Guid.NewGuid();
    private static readonly Guid Sub = Guid.NewGuid();

    private static readonly IReadOnlyList<SourceColumnInfo> Cols = new[]
    {
        new SourceColumnInfo(Ciudad, "Ciudad", DataContainerColumnType.Text),
        new SourceColumnInfo(Monto, "Monto", DataContainerColumnType.Decimal),
        new SourceColumnInfo(Fecha, "Fecha", DataContainerColumnType.Date),
        new SourceColumnInfo(Activo, "Activo", DataContainerColumnType.Boolean),
        new SourceColumnInfo(Sub, "Items", DataContainerColumnType.Submodel)
    };

    private static IReadOnlyDictionary<Guid, string?> Row(string? ciudad, string? monto, string? fecha = null, string? activo = null)
        => new Dictionary<Guid, string?> { [Ciudad] = ciudad, [Monto] = monto, [Fecha] = fecha, [Activo] = activo };

    // ---------- Normalizacion ----------

    [Theory]
    [InlineData("10", "10")]
    [InlineData("10.0", "10")]
    [InlineData(" 10.50 ", "10.5")]
    [InlineData("1,5", "1.5")]
    [InlineData("abc", "abc")]
    [InlineData(null, "")]
    public void Normalize_Decimal_IsCanonical(string? input, string expected)
        => Assert.Equal(expected, DerivedRowsBuilder.Normalize(input, DataContainerColumnType.Decimal));

    [Theory]
    [InlineData("2026-10-22", "2026-10-22")]
    [InlineData("2026-10-22T00:00:00", "2026-10-22")]
    [InlineData("2026-10-22 13:45:00", "2026-10-22T13:45:00")]
    [InlineData("no es fecha", "no es fecha")]
    public void Normalize_Date_IsIso(string input, string expected)
        => Assert.Equal(expected, DerivedRowsBuilder.Normalize(input, DataContainerColumnType.Date));

    [Theory]
    [InlineData("Si", "true")]
    [InlineData("1", "true")]
    [InlineData("NO", "false")]
    [InlineData("0", "false")]
    [InlineData("tal vez", "tal vez")]
    public void Normalize_Boolean_IsTrueFalse(string input, string expected)
        => Assert.Equal(expected, DerivedRowsBuilder.Normalize(input, DataContainerColumnType.Boolean));

    [Fact]
    public void Normalize_Text_TrimsOnly_AndKeepsCase()
        => Assert.Equal("Bogota", DerivedRowsBuilder.Normalize("  Bogota  ", DataContainerColumnType.Text));

    // ---------- Agrupacion y clave ----------

    [Fact]
    public void Build_GroupsByNormalizedKey_SoTenAndTenPointZeroCollapse()
    {
        var spec = new DerivationSpec(Guid.NewGuid(), new[] { Monto }, new[] { new DerivationAggregate(null, DerivationFn.Count, "Cantidad") });
        var result = DerivedRowsBuilder.Build(spec, Cols, new[] { Row("A", "10"), Row("B", "10.0"), Row("C", " 10.00 "), Row("D", "11") });

        Assert.Equal(2, result.Rows.Count);
        var ten = result.Rows.Single(r => r["Clave"] == "10");
        Assert.Equal("3", ten["Cantidad"]);
        Assert.Equal("10", ten["Monto"]);
        Assert.Equal("1", result.Rows.Single(r => r["Clave"] == "11")["Cantidad"]);
    }

    [Fact]
    public void Build_MultipleGroupColumns_KeyIsJoinedAndOrderedDeterministically()
    {
        var spec = new DerivationSpec(Guid.NewGuid(), new[] { Ciudad, Activo }, Array.Empty<DerivationAggregate>());
        var result = DerivedRowsBuilder.Build(spec, Cols, new[]
        {
            Row("Cali", "1", activo: "si"),
            Row("Bogota", "2", activo: "no"),
            Row("Cali", "3", activo: "SI"),
            Row("Bogota", "4", activo: "si")
        });

        // Proyeccion distinta sin agregados: 3 combinaciones, ordenadas por clave.
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal("Bogota | false", result.Rows[0]["Clave"]);
        Assert.Equal("Bogota | true", result.Rows[1]["Clave"]);
        Assert.Equal("Cali | true", result.Rows[2]["Clave"]);
        Assert.Equal("true", result.Rows[2]["Activo"]);
        Assert.Equal(4, result.SourceRowsRead);
    }

    [Fact]
    public void Build_EmptyGroupValue_IsNullInRow_ButStillGroups()
    {
        var spec = new DerivationSpec(Guid.NewGuid(), new[] { Ciudad }, new[] { new DerivationAggregate(null, DerivationFn.Count, "N") });
        var result = DerivedRowsBuilder.Build(spec, Cols, new[] { Row(null, "1"), Row("", "2"), Row("  ", "3"), Row("X", "4") });

        Assert.Equal(2, result.Rows.Count);
        var empty = result.Rows.Single(r => r["Clave"] == "");
        Assert.Null(empty["Ciudad"]);
        Assert.Equal("3", empty["N"]);
    }

    // ---------- Agregados ----------

    [Fact]
    public void Plan_AggregateTypes_CountNumber_SumAvgDecimal_MinMaxInherit()
    {
        var spec = new DerivationSpec(Guid.NewGuid(), new[] { Ciudad }, new[]
        {
            new DerivationAggregate(null, DerivationFn.Count, "N"),
            new DerivationAggregate(Monto, DerivationFn.Sum, "Total"),
            new DerivationAggregate(Monto, DerivationFn.Avg, "Promedio"),
            new DerivationAggregate(Fecha, DerivationFn.Min, "Primera"),
            new DerivationAggregate(Ciudad, DerivationFn.Max, "UltimaCiudad")
        });
        var plan = DerivedRowsBuilder.Plan(spec, Cols);

        Assert.True(plan.Ok, plan.Error);
        var cols = plan.Columns!;
        Assert.Equal(new[] { "Ciudad", "Clave", "N", "Total", "Promedio", "Primera", "UltimaCiudad" }, cols.Select(c => c.Name));
        Assert.Equal(DataContainerColumnType.Text, cols[0].Type);
        Assert.Equal(DerivedColumnRole.GroupBy, cols[0].Role);
        Assert.Equal(DerivedColumnRole.Key, cols[1].Role);
        Assert.Equal(DataContainerColumnType.Number, cols[2].Type);
        Assert.Equal(DataContainerColumnType.Decimal, cols[3].Type);
        Assert.Equal(DataContainerColumnType.Decimal, cols[4].Type);
        Assert.Equal(DataContainerColumnType.Date, cols[5].Type);
        Assert.Equal(DataContainerColumnType.Text, cols[6].Type);
    }

    [Fact]
    public void Build_SumAvgMinMax_ComputeOverNormalizedValues()
    {
        var spec = new DerivationSpec(Guid.NewGuid(), new[] { Ciudad }, new[]
        {
            new DerivationAggregate(Monto, DerivationFn.Sum, "Total"),
            new DerivationAggregate(Monto, DerivationFn.Avg, "Promedio"),
            new DerivationAggregate(Monto, DerivationFn.Min, "Menor"),
            new DerivationAggregate(Monto, DerivationFn.Max, "Mayor"),
            new DerivationAggregate(Fecha, DerivationFn.Min, "Desde"),
            new DerivationAggregate(Fecha, DerivationFn.Max, "Hasta")
        });
        var result = DerivedRowsBuilder.Build(spec, Cols, new[]
        {
            Row("Cali", "10", "2026-01-05"),
            Row("Cali", "2,5", "2025-12-31"),
            Row("Cali", "no numerico", "2026-03-01"),
            Row("Cali", "9", null)
        });

        var cali = Assert.Single(result.Rows);
        Assert.Equal("21.5", cali["Total"]);
        Assert.Equal("7.166667", cali["Promedio"]);
        // Min/Max numericos comparan como numero (no como texto: "10" > "9").
        Assert.Equal("2.5", cali["Menor"]);
        Assert.Equal("10", cali["Mayor"]);
        Assert.Equal("2025-12-31", cali["Desde"]);
        Assert.Equal("2026-03-01", cali["Hasta"]);
    }

    [Fact]
    public void Build_Avg_WithoutNumericValues_IsNull_NotDivisionByZero()
    {
        var spec = new DerivationSpec(Guid.NewGuid(), new[] { Ciudad }, new[]
        {
            new DerivationAggregate(Monto, DerivationFn.Avg, "Promedio"),
            new DerivationAggregate(Monto, DerivationFn.Sum, "Total"),
            new DerivationAggregate(null, DerivationFn.Count, "N")
        });
        var result = DerivedRowsBuilder.Build(spec, Cols, new[] { Row("Cali", null), Row("Cali", ""), Row("Cali", "texto") });

        var cali = Assert.Single(result.Rows);
        Assert.Null(cali["Promedio"]);
        Assert.Null(cali["Total"]);
        Assert.Equal("3", cali["N"]);
    }

    // ---------- Filtro previo ----------

    [Fact]
    public void Build_Filter_AppliesBeforeGrouping_WithTypedComparison()
    {
        var spec = new DerivationSpec(Guid.NewGuid(), new[] { Ciudad },
            new[] { new DerivationAggregate(null, DerivationFn.Count, "N") },
            new[] { new DerivationFilter(Monto, DerivationFilterOp.GreaterThan, "9") });
        var result = DerivedRowsBuilder.Build(spec, Cols, new[] { Row("Cali", "10"), Row("Cali", "100"), Row("Cali", "9"), Row("Bogota", "8") });

        var cali = Assert.Single(result.Rows);
        Assert.Equal("2", cali["N"]);
        Assert.Equal(4, result.SourceRowsRead);
        Assert.Equal(2, result.SourceRowsAfterFilter);
    }

    [Fact]
    public void Build_Filter_EqualsIsCaseInsensitive_AndIsEmptyWorks()
    {
        var spec = new DerivationSpec(Guid.NewGuid(), new[] { Ciudad },
            new[] { new DerivationAggregate(null, DerivationFn.Count, "N") },
            new[] { new DerivationFilter(Ciudad, DerivationFilterOp.Equals, "cali"), new DerivationFilter(Monto, DerivationFilterOp.NotEmpty, null) });
        var result = DerivedRowsBuilder.Build(spec, Cols, new[] { Row("CALI", "1"), Row("Cali", null), Row("Bogota", "1") });

        var only = Assert.Single(result.Rows);
        Assert.Equal("1", only["N"]);
    }

    // ---------- Validacion de la spec ----------

    [Fact]
    public void Plan_RejectsNoGroupColumns()
    {
        var plan = DerivedRowsBuilder.Plan(new DerivationSpec(Guid.NewGuid(), Array.Empty<Guid>(), Array.Empty<DerivationAggregate>()), Cols);
        Assert.False(plan.Ok);
        Assert.Contains("agrupacion", plan.Error);
    }

    [Fact]
    public void Plan_RejectsSubmodelGroupColumn()
    {
        var plan = DerivedRowsBuilder.Plan(new DerivationSpec(Guid.NewGuid(), new[] { Sub }, Array.Empty<DerivationAggregate>()), Cols);
        Assert.False(plan.Ok);
        Assert.Contains("Items", plan.Error);
    }

    [Fact]
    public void Plan_RejectsSumOnTextColumn()
    {
        var plan = DerivedRowsBuilder.Plan(new DerivationSpec(Guid.NewGuid(), new[] { Ciudad },
            new[] { new DerivationAggregate(Ciudad, DerivationFn.Sum, "X") }), Cols);
        Assert.False(plan.Ok);
        Assert.Contains("numericas", plan.Error);
    }

    [Fact]
    public void Plan_RejectsDuplicateTargetNames_AndKeyCollision()
    {
        var dup = DerivedRowsBuilder.Plan(new DerivationSpec(Guid.NewGuid(), new[] { Ciudad },
            new[] { new DerivationAggregate(null, DerivationFn.Count, "ciudad") }), Cols);
        Assert.False(dup.Ok);

        var key = DerivedRowsBuilder.Plan(new DerivationSpec(Guid.NewGuid(), new[] { Ciudad },
            Array.Empty<DerivationAggregate>(), KeyColumnName: "Ciudad"), Cols);
        Assert.False(key.Ok);
        Assert.Contains("clave", key.Error);
    }

    [Fact]
    public void Plan_RejectsMissingSourceColumn()
    {
        var plan = DerivedRowsBuilder.Plan(new DerivationSpec(Guid.NewGuid(), new[] { Guid.NewGuid() }, Array.Empty<DerivationAggregate>()), Cols);
        Assert.False(plan.Ok);
        Assert.Contains("ya no existe", plan.Error);
    }

    // ---------- Persistencia de la spec ----------

    [Fact]
    public void Spec_RoundTripsThroughJson()
    {
        var spec = new DerivationSpec(Guid.NewGuid(), new[] { Ciudad, Activo },
            new[] { new DerivationAggregate(Monto, DerivationFn.Avg, "Promedio"), new DerivationAggregate(null, DerivationFn.Count, "N") },
            new[] { new DerivationFilter(Monto, DerivationFilterOp.GreaterThan, "0") },
            KeyColumnName: "Llave");

        var back = DerivationSpec.FromJson(spec.ToJson());

        Assert.NotNull(back);
        Assert.Equal(spec.SourceTableId, back!.SourceTableId);
        Assert.Equal(spec.GroupBy, back.GroupBy);
        Assert.Equal(spec.Aggregates, back.Aggregates);
        Assert.Equal(spec.Filter, back.Filter);
        Assert.Equal("Llave", back.KeyColumnName);
        Assert.Null(DerivationSpec.FromJson("{ esto no es json"));
        Assert.Null(DerivationSpec.FromJson(null));
    }
}
