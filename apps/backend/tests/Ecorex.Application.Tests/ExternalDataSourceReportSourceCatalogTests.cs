using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Panels;
using Ecorex.Application.Reporting.Sources;

namespace Ecorex.Application.Tests;

/// <summary>
/// Catalogo de la fuente nativa 'Servidores de datos' (<see cref="ExternalDataSourceReportSource"/>, sobre
/// external_data_sources, ADR-0084). Describe() es PURO (no toca BD). Verifica: campos expuestos, que
/// Motor/Acceso/Estado son agrupables y filtrables, que un PanelSpec Main "Servidores de datos" agrupa por
/// Motor y filtra por Estado, y que NUNCA se expone la cadena de conexion.
/// </summary>
public class ExternalDataSourceReportSourceCatalogTests
{
    private static ReportSourceDescriptor Describe() => new ExternalDataSourceReportSource(null!).Describe();

    [Fact]
    public void Catalog_HasKeyAndDisplayName()
    {
        var d = Describe();
        Assert.Equal("native:externalsource", d.Key);
        Assert.Equal("Servidores de datos", d.DisplayName);
        Assert.Equal(ReportSourceKind.Native, d.Kind);
    }

    [Theory]
    [InlineData("Name", "Nombre", ReportFieldType.Text)]
    [InlineData("Provider", "Motor", ReportFieldType.Text)]
    [InlineData("Acceso", "Acceso", ReportFieldType.Text)]
    [InlineData("Estado", "Estado", ReportFieldType.Text)]
    [InlineData("Datasets", "Datasets", ReportFieldType.Number)]
    [InlineData("LastValidatedAt", "UltimaValidacion", ReportFieldType.Date)]
    [InlineData("Description", "Descripcion", ReportFieldType.Text)]
    [InlineData("CreatedAt", "Creada", ReportFieldType.Date)]
    [InlineData("UpdatedAt", "Actualizada", ReportFieldType.Date)]
    public void Catalog_ExposesField(string key, string display, ReportFieldType type)
    {
        var field = Describe().FindField(key);
        Assert.NotNull(field);
        Assert.Equal(display, field!.DisplayName);
        Assert.Equal(type, field.Type);
    }

    [Theory]
    [InlineData("Provider")]
    [InlineData("Acceso")]
    [InlineData("Estado")]
    public void Catalog_MotorAccesoEstado_AreGroupableAndFilterable(string key)
    {
        var field = Describe().FindField(key);
        Assert.NotNull(field);
        Assert.True(field!.CanGroup);
        Assert.True(field.CanFilter);
    }

    [Fact]
    public void Catalog_NeverExposesConnectionString()
    {
        var d = Describe();
        Assert.Null(d.FindField("ConnectionString"));
        Assert.Null(d.FindField("ConnectionStringEncrypted"));
        foreach (var f in d.Fields)
        {
            var probe = (f.Key + " " + f.DisplayName).ToLowerInvariant();
            Assert.DoesNotContain("connection", probe);
            Assert.DoesNotContain("conexion", probe);
            Assert.DoesNotContain("cadena", probe);
            Assert.DoesNotContain("password", probe);
            Assert.DoesNotContain("secret", probe);
        }
    }

    [Fact]
    public void PanelSpec_CanGroupByMotor_AndFilterByEstado()
    {
        var spec = PanelSpec.FromJson(@"{
          ""title"": ""Servidores de datos"",
          ""sources"": { ""main"": { ""container"": ""Servidores de datos"" } },
          ""filters"": [ { ""field"": ""Estado"", ""control"": ""dropdown"" } ],
          ""widgets"": [ { ""type"": ""bar"", ""dim"": ""Motor"", ""agg"": ""count"" } ]
        }")!;

        var errors = PanelSpecValidator.Validate(spec, new[] { Describe() });

        Assert.Empty(errors);
    }
}
