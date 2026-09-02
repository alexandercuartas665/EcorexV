using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Panels;
using Ecorex.Application.Reporting.Sources;

namespace Ecorex.Application.Tests;

/// <summary>
/// Catalogo de la fuente nativa 'Conexiones' (<see cref="DataConnectorReportSource"/>, ADR-0084). Describe()
/// es PURO (no toca BD), asi que se prueba sin contexto. Verifica: (a) la fuente expone los metadatos de
/// conexion sin credenciales, (b) Tipo/Motor/Activa son agrupables y filtrables, (c) un PanelSpec con Main
/// "Conexiones" puede agrupar por Tipo y filtrar por Motor/Activa contra el catalogo, y (d) que NUNCA se
/// expone la credencial ni el usuario.
/// </summary>
public class DataConnectorReportSourceCatalogTests
{
    private static ReportSourceDescriptor Describe() => new DataConnectorReportSource(null!).Describe();

    [Fact]
    public void Catalog_HasKeyAndDisplayName()
    {
        var d = Describe();
        Assert.Equal("native:dataconnector", d.Key);
        Assert.Equal("Conexiones", d.DisplayName);
        Assert.Equal(ReportSourceKind.Native, d.Kind);
    }

    [Theory]
    [InlineData("Name", "Nombre", ReportFieldType.Text)]
    [InlineData("Kind", "Tipo", ReportFieldType.Text)]
    [InlineData("DbEngine", "Motor", ReportFieldType.Text)]
    [InlineData("Host", "Host", ReportFieldType.Text)]
    [InlineData("Port", "Puerto", ReportFieldType.Number)]
    [InlineData("DatabaseName", "BaseDatos", ReportFieldType.Text)]
    [InlineData("EndpointUrl", "Endpoint", ReportFieldType.Text)]
    [InlineData("HttpMethod", "Metodo", ReportFieldType.Text)]
    [InlineData("AuthKind", "Auth", ReportFieldType.Text)]
    [InlineData("Container", "Contenedor", ReportFieldType.Text)]
    [InlineData("IsActive", "Activa", ReportFieldType.Boolean)]
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
    [InlineData("Kind")]
    [InlineData("DbEngine")]
    [InlineData("IsActive")]
    public void Catalog_TipoMotorActiva_AreGroupableAndFilterable(string key)
    {
        var field = Describe().FindField(key);
        Assert.NotNull(field);
        Assert.True(field!.CanGroup);
        Assert.True(field.CanFilter);
    }

    [Fact]
    public void Catalog_NeverExposesCredentials()
    {
        var d = Describe();
        // No hay campos de credencial ni usuario.
        Assert.Null(d.FindField("CredentialsEncrypted"));
        Assert.Null(d.FindField("Credentials"));
        Assert.Null(d.FindField("Username"));
        // Ningun campo (clave o nombre) alude a credencial/contrasena/secreto.
        foreach (var f in d.Fields)
        {
            var probe = (f.Key + " " + f.DisplayName).ToLowerInvariant();
            Assert.DoesNotContain("credencial", probe);
            Assert.DoesNotContain("credential", probe);
            Assert.DoesNotContain("password", probe);
            Assert.DoesNotContain("contrasena", probe);
            Assert.DoesNotContain("secret", probe);
            Assert.DoesNotContain("secreto", probe);
        }
    }

    [Fact]
    public void PanelSpec_CanGroupByTipo_AndFilterByMotorAndActiva()
    {
        // Fuente principal por su nombre visible ("Conexiones"); dim/filtros por DisplayName.
        var spec = PanelSpec.FromJson(@"{
          ""title"": ""Servidores y conexiones"",
          ""sources"": { ""main"": { ""container"": ""Conexiones"" } },
          ""filters"": [
            { ""field"": ""Motor"", ""control"": ""dropdown"" },
            { ""field"": ""Activa"", ""control"": ""dropdown"" }
          ],
          ""widgets"": [ { ""type"": ""bar"", ""dim"": ""Tipo"", ""agg"": ""count"" } ]
        }")!;

        var errors = PanelSpecValidator.Validate(spec, new[] { Describe() });

        Assert.Empty(errors);
    }
}
